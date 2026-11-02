using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{
    private const string StudyShipFormulationNote =
        "информационная чувствительность к формулировке силы при данной запечённой эфемериде, "
        + "не мера ошибки эталона и не ошибка интегратора. На 1000 витках 8.5 м "
        + "(рост нестепенной, локальные показатели 2.9-3.9); достаточно сил ~2e-13 м/с^2 "
        + "для такого сдвига за 64 суток. Production и certified согласуются на 1.2 см, "
        + "dd решает ОДУ с другим непрямым членом.";

    private sealed class StudyShipMatrixCase
    {
        public string Label;
        public StarSystem Sys;
        public int Central;
        public Vector3d P0;
        public Vector3d V0;
        public double Mass0;
        public double Seconds;
        public bool Perturbed;
        public bool Thrust;
        public Vector3d ThrustDirection;
        public double ThrustStart;
        public double ThrustSeconds;
        public double ThrustNewtons;
        public double ThrustIsp;
        public double ThrustRampSeconds;
        public bool AllowTaylor;
        public double Tau;
        public double StepScale;
        public DragSource Drag;
        public double WorldChunkSeconds = 600d;
        public double ReferenceStepSeconds;
        public double ScaleLength = 1d;
        public bool UseTaylorDdReference;
        public double AccuracyTarget = 1d;
        public double AlternativeTarget = 10d;
        public bool DiagNoMoon;
        public bool DiagFrozenMoon;
        public double DiagFrozenStart;
        public double DiagFrozenEnd;
        public Vector3d DiagFrozenMoonAccel;

        public StudyShipMatrixCase Clone()
        {
            return (StudyShipMatrixCase)MemberwiseClone();
        }
    }

    private sealed class StudyShipMatrixRef
    {
        public Vector3d P;
        public Vector3d V;
        public double M;
        public OrbitalElements Elements;
    }

    private sealed class StudyShipMatrixRefs
    {
        public StudyShipMatrixRef Local;
        public double LocalError;
        public string LocalKind;
        public bool LocalValid;
        public bool LocalAsymptotic;
        public bool LocalResolved;
        public double LocalRatio;
        public double LocalOrder;
        public double LocalFinestDelta;
        public double[] CheckpointTimes;
        public Vector3d[] CheckpointP;
        public Vector3d[] CheckpointV;
        public StudyShipMatrixRef Dd;
        public double DdSelf;
        public double DdError;
        public double DdRatio;
        public string DdKind;
        public bool HasDd;
        public double Formulation;
        public bool HasFormulation;

        /// <summary>Self-convergence of the certified local reference alone.</summary>
        public double CertifiedFloor
        {
            get
            {
                if (LocalAsymptotic && double.IsFinite(LocalError))
                {
                    return LocalError;
                }

                return LocalResolved ? LocalFinestDelta : double.NaN;
            }
        }

        /// <summary>
        /// Ranking floor: the certified local reference's own convergence. The
        /// local DD cross-check is a consistency check on the same force, not an
        /// independent error source, so it is not folded into the floor; the gate
        /// requires it to stay within the certified floor's own scale.
        /// </summary>
        public double ReferenceFloor
        {
            get
            {
                return CertifiedFloor;
            }
        }
    }

    private sealed class StudyShipMatrixBaseline
    {
        public double Production;
        public double BestLocal;
        public double Formulation;

        public StudyShipMatrixBaseline(double production, double bestLocal, double formulation)
        {
            Production = production;
            BestLocal = bestLocal;
            Formulation = formulation;
        }
    }

    private static readonly Dictionary<string, StudyShipMatrixBaseline> StudyShipMatrixBaselines =
        new Dictionary<string, StudyShipMatrixBaseline>
        {
            // Rebased 2026-11-01 to the final max-over-trajectory run (exact
            // checkpoint landings, rank = max(dp, reference floor)). The old
            // final-point numbers and the first max-metric pass taken with an
            // inexact sample flush are not comparable and are discarded: with the
            // exact flush the production max equals its final error on most cases
            // (the earlier 3.7e-2 for LEO 1 d was a sampling artifact).
            { "Earth LEO 400 km circular", new StudyShipMatrixBaseline(2.22228e-5, 1.48325e-6, 1.464e-3) },
            { "Earth LEO 400 km polar", new StudyShipMatrixBaseline(9.3476e-6, 1.90441e-6, 1.643e-3) },
            { "Earth LEO 400 km sun-synchronous", new StudyShipMatrixBaseline(1.87243e-5, 1.09409e-5, 1.679e-3) },
            { "Earth GTO 400 x 40000 km", new StudyShipMatrixBaseline(3.87613e-5, 4.38584e-5, 1.924e-2) },
            { "Earth Molniya 500 x 40000 km", new StudyShipMatrixBaseline(1.27417e-2, 1.12412e-4, 1.754) },
            // 2026-11-02: rebased for the 5-level certified reference (the extra
            // level is the returned solution; deltas are measured without forced
            // checkpoints). The old floors were optimistic by 3-8x on these rows.
            { "Earth GEO 35786 km", new StudyShipMatrixBaseline(2.646e-4, 6.91894e-5, 1.876e-1) },
            { "Sun 1 AU circular", new StudyShipMatrixBaseline(6.21497e-2, 7.17697e-4, 7.339e-4) },
            { "Sun 0.5 x 2 AU eccentric", new StudyShipMatrixBaseline(2.34371e-2, 1.09566e-3, 1.113e-2) },
            { "Earth 400 km + J2", new StudyShipMatrixBaseline(9.1382e-6, 1.08785e-6, double.NaN) },
            { "Earth 250 km + atmosphere drag", new StudyShipMatrixBaseline(2.23923e-5, 2.21451e-5, double.NaN) },
            { "Earth LEO 400 km + 600 s prograde burn", new StudyShipMatrixBaseline(9.2864e-2, 2.47899e-5, double.NaN) },
            { "Earth-Moon flyby inside lunar SOI", new StudyShipMatrixBaseline(2.55395e-3, 1.80254e-5, 7.697e-4) },
            { "Earth LEO 400 km, 1000 orbits (~64 d)", new StudyShipMatrixBaseline(5.54329e-2, 5.54329e-2, 8.484) },
            { "Earth HEO 300 x 120000 km", new StudyShipMatrixBaseline(2.01437e-4, 1.03298e-4, 7.575e-1) },
            { "Earth hyperbolic flyby e=1.5", new StudyShipMatrixBaseline(9.65466e-6, 1.81942e-6, 1.129e-3) },
            // production rebased 2026-11-02 with the 5-level reference (3.943e-3).
            { "Earth LEO 400 km, 30 d, world chunk 1 d", new StudyShipMatrixBaseline(3.943e-3, 1.0288e-3, 8.410e-1) },
            { "Earth LEO 400 km + unaligned 600 s burn", new StudyShipMatrixBaseline(1.94127e-1, 1.85835e-1, double.NaN) }
        };

    private sealed class StudyShipMatrixRow
    {
        public string Method;
        public string Config;
        public string Frame;
        /// <summary>Max |p(t) - ref(t)| over the sampled trajectory (primary metric).</summary>
        public double Dp;
        public double Dv;
        /// <summary>Final-point error; diagnostic only, Dp is the ranked metric.</summary>
        public double DpFinal;
        public double DvFinal;
        public double Da;
        public double De;
        public double DEnergy;
        public long Calls;
        /// <summary>
        /// RHS-equivalent cost: Calls for one-force-evaluation methods, Calls*order
        /// for Taylor (one "call" is one jet-derivative order, not one RHS).
        /// </summary>
        public long Cost;
        public long Steps;
        public double Ms;
        public int Reps = 1;
        public int SampleCount;
        public Vector3d P;
        public Vector3d V;
        /// <summary>Trajectory checkpoints (same times for every row of a case).</summary>
        public Vector3d[] SamplesP;
        public bool Finite;
        public string Note;
    }

    private sealed class StudyShipRun
    {
        public Vector3d P;
        public Vector3d V;
        public double M;
        public long Calls;
        public long Steps;
        public double Ms;
        public int Reps = 1;
        public Vector3d[] SamplesP;
        public Vector3d[] SamplesV;
    }

    private sealed class StudyShipMatrixAcc
    {
        public readonly StudyShipMatrixCase Case;
        public long Calls;

        public StudyShipMatrixAcc(StudyShipMatrixCase c)
        {
            Case = c;
        }

        public SpacecraftIntegrationState Evaluate(SpacecraftIntegrationState s, double t)
        {
            Calls++;
            Vector3d gravity = StudyShipMatrixLocalAcceleration(Case, s.Position, t);
            Vector3d force = Vector3d.Zero;
            double flow = 0d;
            if (Case.Drag != null)
            {
                Case.Sys.EvaluateBodyState(Case.Sys.AllBodies[Case.Central], t,
                    out Vector3d bp, out Vector3d bv);
                DynamicsContribution drag = Case.Drag.Evaluate(
                    bp + s.Position, bv + s.Velocity, s.Mass, t);
                force += drag.Force;
                flow += drag.MassFlow;
            }

            double throttle = StudyShipMatrixThrottle(Case, t);
            if (throttle > 0d)
            {
                force += Case.ThrustDirection * (Case.ThrustNewtons * throttle);
                flow += Case.ThrustNewtons * throttle / (Case.ThrustIsp * 9.80665d);
            }

            double mass = s.Mass > 1e-9d ? s.Mass : 1e-9d;
            return new SpacecraftIntegrationState
            {
                Position = s.Velocity,
                Velocity = gravity + (force / mass),
                Mass = -flow
            };
        }

        public void EvaluateArray(double[] s, double t, double[] ds)
        {
            var state = new SpacecraftIntegrationState
            {
                Position = new Vector3d(s[0], s[1], s[2]),
                Velocity = new Vector3d(s[3], s[4], s[5]),
                Mass = s[6]
            };
            SpacecraftIntegrationState d = Evaluate(state, t);
            ds[0] = d.Position.X;
            ds[1] = d.Position.Y;
            ds[2] = d.Position.Z;
            ds[3] = d.Velocity.X;
            ds[4] = d.Velocity.Y;
            ds[5] = d.Velocity.Z;
            ds[6] = d.Mass;
        }
    }

    private delegate void StudyShipMatrixAccFn(double[] s, double t, double[] ds);

    private static double StudyShipMatrixThrottle(StudyShipMatrixCase c, double t)
    {
        if (!c.Thrust)
        {
            return 0d;
        }

        double start = c.ThrustStart;
        double end = start + c.ThrustSeconds;
        // Right limit at the burn start: a forward step that begins exactly at
        // `start` must see the engine on, otherwise the burn loses a full step.
        if (t < start || t >= end)
        {
            return 0d;
        }

        double ramp = c.ThrustRampSeconds;
        if (ramp <= 0d)
        {
            return 1d;
        }

        if (t < start + ramp)
        {
            return (t - start) / ramp;
        }

        if (t > end - ramp)
        {
            return (end - t) / ramp;
        }

        return 1d;
    }

    private static string StudyShipMatrixNote(Exception ex)
    {
        return (ex.GetType().Name + " " + ex.Message)
            .Replace(',', ';')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    private static string StudyShipMatrixResultsDir()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "Assets"))
                && Directory.Exists(Path.Combine(dir, "Tools")))
            {
                return Path.Combine(dir, "Tools", "P1bTests", "Study", "integrators", "results");
            }

            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "Study", "integrators", "results");
    }

    private static StarSystem StudyShipMatrixSystem(
        bool j2, bool atmosphere, bool bake = true)
    {
        StarSystem sys = StudyRealSolar7();
        OrbitingBody earth = sys.AllBodies[1];
        earth.RotationPeriodSeconds = 86164.0905d;
        if (j2)
        {
            earth.J2 = 1.08263e-3d;
        }

        if (atmosphere)
        {
            earth.Atmosphere = new AtmosphereProfile
            {
                TopAltitudeMeters = 2e6d,
                SeaLevelDensityKgPerCubicMeter = 1.225d,
                ScaleHeightMeters = 8500d
            };
        }

        if (bake)
        {
            StudyNBody nb = StudyMakeNBody(sys);
            StudyBake("ship matrix " + (j2 ? "J2" : "gravity")
                + (atmosphere ? "+atmosphere" : ""),
                sys, nb, (j2 || atmosphere) ? 0.25d : 1.2d, 0d);
        }

        return sys;
    }

    private static double StudyShipMatrixSeamJump(StarSystem sys, string label)
    {
        Console.WriteLine();
        Console.WriteLine("=== baked seam jumps: " + label + " ===");
        double worst = 0d;
        double worstVelocity = 0d;
        string worstBody = "none";
        foreach (OrbitingBody body in sys.AllBodies)
        {
            BakedEphemeris baked = body.Baked;
            if (baked == null || baked.SegmentCount < 2)
            {
                Console.WriteLine("  " + body.Name + ": no baked segments"
                    + (body.Parent == null ? " (root)" : ""));
                continue;
            }

            double bodyWorst = 0d;
            double bodyWorstV = 0d;
            for (int seg = 0; seg < baked.SegmentCount - 1; seg++)
            {
                double t = baked.SegmentEndSeconds(seg);
                const double eps = 1e-6d;
                body.EvaluateWorldState(t - eps, out Vector3d p1, out Vector3d v1);
                body.EvaluateWorldState(t + eps, out Vector3d p2, out Vector3d v2);
                double jump = (p2 - p1).Magnitude;
                double vjump = (v2 - v1).Magnitude;
                if (jump > bodyWorst)
                {
                    bodyWorst = jump;
                }

                if (vjump > bodyWorstV)
                {
                    bodyWorstV = vjump;
                }
            }

            Console.WriteLine("  " + body.Name + ": max seam jump "
                + bodyWorst.ToString("E3", CultureInfo.InvariantCulture) + " m, velocity "
                + bodyWorstV.ToString("E3", CultureInfo.InvariantCulture) + " m/s");
            Check(bodyWorstV < 1e-2d,
                "matrix seam velocity jump " + label + " " + body.Name,
                bodyWorstV.ToString("E3", CultureInfo.InvariantCulture) + " m/s < 1E-02 m/s");
            if (bodyWorst > worst)
            {
                worst = bodyWorst;
                worstBody = body.Name;
            }

            if (bodyWorstV > worstVelocity)
            {
                worstVelocity = bodyWorstV;
            }
        }

        Console.WriteLine("  worst: " + worstBody + " "
            + worst.ToString("E3", CultureInfo.InvariantCulture) + " m, velocity "
            + worstVelocity.ToString("E3", CultureInfo.InvariantCulture) + " m/s");
        return worst;
    }

    private static void StudyShipMatrixOrbit(
        StarSystem sys, int central, double rp, double ra,
        double incDeg, double nodeDeg, double argpDeg,
        out Vector3d p, out Vector3d v)
    {
        double mu = sys.AllBodies[central].ResolveStandardGravitationalParameter();
        double a = 0.5d * (rp + ra);
        double vp = Math.Abs(ra - rp) < 1e-6d * rp
            ? Math.Sqrt(mu / rp)
            : Math.Sqrt(mu * ((2d / rp) - (1d / a)));
        Vector3d po = new Vector3d(rp, 0d, 0d);
        Vector3d vo = new Vector3d(0d, vp, 0d);
        double node = nodeDeg * Math.PI / 180d;
        double inc = incDeg * Math.PI / 180d;
        double argp = argpDeg * Math.PI / 180d;
        p = KeplerMath.RotateOrbitalToWorld(po, node, inc, argp);
        v = KeplerMath.RotateOrbitalToWorld(vo, node, inc, argp);
    }

    private static double StudyShipMatrixTau(double rp, double mu)
    {
        return 2d * Math.PI * Math.Sqrt(rp * rp * rp / mu);
    }

    private static double[] StudyShipMatrixSteps(StudyShipMatrixCase c)
    {
        // ONE grid for every family: the same h values are used for RK4, DOPRI5,
        // PEFRL, Y6, Y6C and WH, so the Pareto compares like with like. The finest
        // point is bounded by a force-evaluation budget that assumes the cheapest
        // method (4 evals/step); a high-order method simply pays more calls at the
        // same h, and that cost is visible in the table.
        if (c.Thrust)
        {
            return new[] { 1d, 2d, 4d, 8d };
        }

        double hMax = Math.Max(0.5d, Math.Min(4d * 86400d, c.Tau * c.StepScale / 8d));
        double hMin = Math.Max(0.5d, Math.Max(
            c.Tau * c.StepScale / 8192d,
            c.Seconds * 4d / 400000d));
        if (!(hMin < hMax))
        {
            return new[] { hMax };
        }

        const int count = 4;
        var steps = new double[count];
        double ratio = Math.Pow(hMax / hMin, 1d / (count - 1));
        for (int i = 0; i < count; i++)
        {
            steps[i] = hMin * Math.Pow(ratio, i);
        }

        steps[count - 1] = hMax;
        return steps;
    }

    private static double[] StudyShipMatrixCheckpointTimes(StudyShipMatrixCase c)
    {
        // One checkpoint per orbit up to 256, at least 8: the error of a long run
        // is a phase error that grows and shrinks, and the final point alone can
        // land on a lucky phase (RK4 at 1000 orbits reads 1.6e6, 8.1e6 and
        // 6.3e5 m for neighbouring steps). Candidate and reference both step
        // exactly on these times, so no interpolation enters the metric.
        int count = (int)Math.Round(c.Seconds / Math.Max(c.Tau, 1d));
        count = Math.Max(8, Math.Min(256, count));
        var times = new double[count];
        for (int i = 0; i < count; i++)
        {
            times[i] = StudyShipT0 + c.Seconds * (i + 1) / count;
        }

        times[count - 1] = StudyShipT0 + c.Seconds;
        return times;
    }

    private static StudyShipMatrixCase StudyShipMatrixCaseOrbit(
        string label, StarSystem sys, int central, double rp, double ra,
        double incDeg, double nodeDeg, double argpDeg, double seconds,
        bool perturbed, bool allowTaylor, double stepScale)
    {
        StudyShipMatrixOrbit(sys, central, rp, ra, incDeg, nodeDeg, argpDeg,
            out Vector3d p, out Vector3d v);
        double mu = sys.AllBodies[central].ResolveStandardGravitationalParameter();
        return new StudyShipMatrixCase
        {
            Label = label,
            Sys = sys,
            Central = central,
            P0 = p,
            V0 = v,
            Mass0 = 1000d,
            Seconds = seconds,
            Perturbed = perturbed,
            AllowTaylor = allowTaylor,
            Tau = StudyShipMatrixTau(rp, mu),
            StepScale = stepScale,
            Drag = perturbed && sys.AllBodies[central].Atmosphere != null
                ? new DragSource(sys.AllBodies[central])
                : null
        };
    }

    private static Vector3d StudyShipMatrixLocalAcceleration(
        StudyShipMatrixCase c, Vector3d relP, double t)
    {
        if (!c.DiagNoMoon && !c.DiagFrozenMoon)
        {
            // Same force the production local-frame step uses; the certified reference
            // and the candidates share the force model, the integrator is independent.
            return c.Sys.EvaluateShipAccelerationLocal(relP, c.Sys.AllBodies[c.Central], t);
        }

        Vector3d full = c.Sys.EvaluateShipAccelerationLocal(
            relP, c.Sys.AllBodies[c.Central], t);
        Vector3d moon = StudyShipMatrixMoonAcceleration(c, relP, t);
        if (c.DiagNoMoon)
        {
            return full - moon;
        }

        return t >= c.DiagFrozenStart && t <= c.DiagFrozenEnd
            ? full - moon + c.DiagFrozenMoonAccel
            : full;
    }

    private static Vector3d StudyShipMatrixMoonAcceleration(
        StudyShipMatrixCase c, Vector3d relP, double t)
    {
        OrbitingBody moon = null;
        for (int i = 0; i < c.Sys.AllBodies.Count; i++)
        {
            if (c.Sys.AllBodies[i].Name.IndexOf("Moon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                moon = c.Sys.AllBodies[i];
                break;
            }
        }

        if (moon == null)
        {
            return Vector3d.Zero;
        }

        c.Sys.EvaluateBodyState(c.Sys.AllBodies[c.Central], t,
            out Vector3d centralPos, out _);
        c.Sys.EvaluateBodyState(moon, t, out Vector3d moonPos, out _);
        double mu = moon.ResolveStandardGravitationalParameter();
        Vector3d dc = centralPos - moonPos;
        return PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                dc + relP, Vector3d.Zero, mu)
            - PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                dc, Vector3d.Zero, mu);
    }
    private static StudyShipMatrixRef StudyShipMatrixRefFromState(
        StudyShipMatrixCase c, Vector3d p, Vector3d v, double m)
    {
        return new StudyShipMatrixRef
        {
            P = p,
            V = v,
            M = m,
            Elements = OrbitalElements.FromState(
                p, v, c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter())
        };
    }

    private static StudyShipMatrixRef StudyShipMatrixLocalReference(
        StudyShipMatrixCase c, out double selfError, out double refError, out string kind)
    {
        StudyShipCertifiedRef cert = StudyShipCertifiedReference(c, c.Central == 1 && c.Seconds > 30d * 86400d ? 8d : 24d);
        selfError = cert.PositionError;
        refError = cert.PositionError;
        kind = cert.Detail + (cert.Valid ? "" : " [INVALID]");
        return StudyShipMatrixRefFromState(c, cert.P, cert.V, cert.M);
    }

    private static StudyShipMatrixRef StudyShipMatrixDdReference(
        StudyShipMatrixCase c, out double selfError, out double refError,
        out double ratio, out string kind)
    {
        OrbitingBody central = c.Sys.AllBodies[c.Central];
        if (c.UseTaylorDdReference)
        {
            StudyShipDdGoldenFile golden = StudyShipDdGolden(c);
            selfError = golden.SelfErrorM;
            refError = golden.SelfErrorM;
            ratio = double.PositiveInfinity;
            kind = "Taylor-dd K" + golden.OrderA.ToString(CultureInfo.InvariantCulture)
                + "/K" + golden.OrderB.ToString(CultureInfo.InvariantCulture)
                + " (golden " + Path.GetFileName(StudyShipDdGoldenPath()) + ")";
            return StudyShipMatrixRefFromState(c,
                new Vector3d(golden.PRel[0], golden.PRel[1], golden.PRel[2]),
                new Vector3d(golden.VRel[0], golden.VRel[1], golden.VRel[2]), c.Mass0);
        }

        double h = c.ReferenceStepSeconds > 0d
            ? c.ReferenceStepSeconds
            : Math.Max(2.5d, Math.Min(600d, c.Tau / 1000d));
        // Same local force and frame as the certified reference. A world-frame DD
        // run is NOT a valid cross-check here: its body positions are doubles, and
        // the ~1e-5 m absolute-coordinate noise makes its self-convergence ratio
        // ~1 even at a 5 s step, which is the size of the reported 1e-3..8.5 m
        // "formulation difference".
        var r1 = StudyShipLocalY6CDd(
            c.Sys, c.Central, c.P0, c.V0, StudyShipT0, c.Seconds / 86400d, h);
        var r2 = StudyShipLocalY6CDd(
            c.Sys, c.Central, c.P0, c.V0, StudyShipT0, c.Seconds / 86400d, h * 0.5d);
        selfError = (r1.p - r2.p).Magnitude;
        var r3 = StudyShipLocalY6CDd(
            c.Sys, c.Central, c.P0, c.V0, StudyShipT0, c.Seconds / 86400d, h * 0.25d);
        double d23 = (r2.p - r3.p).Magnitude;
        ratio = selfError / Math.Max(d23, 1e-30d);
        // Y6C is 6th order (~64). A larger observed ratio is super-convergence on
        // this arc, not a failed measurement; a smaller one is not asymptotic.
        bool asymptotic = ratio >= 32d;
        refError = asymptotic
            ? selfError / Math.Max(ratio - 1d, 1d)
            : double.NaN;

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "local DD cross-check {0}: d(h,h/2)={1:E3} m d(h/2,h/4)={2:E3} m ratio={3:F2}{4}",
            c.Label, selfError, d23, ratio,
            asymptotic ? "" : " (non-asymptotic; its own self-difference is the DD uncertainty)"));
        kind = "local Y6C-dd h=" + h.ToString("G6", CultureInfo.InvariantCulture)
            + (asymptotic
                ? " s (est err " + refError.ToString("E2", CultureInfo.InvariantCulture) + " m)"
                : " s (non-asymptotic)");

        return StudyShipMatrixRefFromState(c, r2.p, r2.v, c.Mass0);
    }

    private static StudyShipMatrixRefs StudyShipMatrixBuildReferences(StudyShipMatrixCase c)
    {
        var refs = new StudyShipMatrixRefs();
        refs.CheckpointTimes = StudyShipMatrixCheckpointTimes(c);
        refs.CheckpointP = new Vector3d[refs.CheckpointTimes.Length];
        refs.CheckpointV = new Vector3d[refs.CheckpointTimes.Length];
        StudyShipCertifiedRef cert = StudyShipCertifiedReference(
            c, c.Central == 1 && c.Seconds > 30d * 86400d ? 8d : 24d,
            5, refs.CheckpointTimes, refs.CheckpointP, refs.CheckpointV);
        refs.Local = StudyShipMatrixRefFromState(c, cert.P, cert.V, cert.M);
        refs.LocalError = cert.PositionError;
        refs.LocalKind = cert.Detail;
        refs.LocalValid = cert.Valid;
        refs.LocalAsymptotic = cert.Asymptotic;
        refs.LocalResolved = cert.Resolved;
        refs.LocalRatio = cert.Ratio;
        refs.LocalOrder = cert.Order;
        refs.LocalFinestDelta = cert.FinestDelta;

        // The DD world-frame run is a cross-check of the certified local reference,
        // never the grading reference: its own convergence is broken by the baked
        // ephemeris (ratios 1.0-4.0 instead of 64), so it cannot certify accuracy.
        if (!c.Perturbed && !c.Thrust)
        {
            refs.Dd = StudyShipMatrixDdReference(
                c, out refs.DdSelf, out refs.DdError, out refs.DdRatio, out refs.DdKind);
            refs.HasDd = true;
            refs.Formulation = (refs.Local.P - refs.Dd.P).Magnitude;
            refs.HasFormulation = true;
        }

        return refs;
    }

    private static StudyShipRun StudyShipMatrixRepeat(Func<StudyShipRun> once)
    {
        StudyShipRun best = null;
        double bestMs = double.PositiveInfinity;
        int reps = 0;
        while (true)
        {
            StudyShipRun run = once();
            reps++;
            if (run.Ms < bestMs)
            {
                best = run;
                bestMs = run.Ms;
            }

            // Wall-clock noise is larger than the differences between methods;
            // min-of-3 makes the cheap rows comparable. Expensive rows (>250 ms)
            // are run once: tripling them would dominate the suite for no gain.
            if (reps >= 3 || bestMs >= 250d)
            {
                break;
            }
        }

        best.Ms = bestMs;
        best.Reps = reps;
        return best;
    }

    private static StudyShipRun StudyShipMatrixAdaptive(
        StudyShipMatrixCase c, bool dop853, double posTol, double velTol, double massTol,
        double rtol, double[] checkpoints = null)
    {
        return StudyShipMatrixRepeat(() =>
        {
            var acc = new StudyShipMatrixAcc(c);
            var run = new StudyShipRun
            {
                SamplesP = checkpoints != null ? new Vector3d[checkpoints.Length] : null,
                SamplesV = checkpoints != null ? new Vector3d[checkpoints.Length] : null,
                Steps = -1L
            };
            var start = new SpacecraftIntegrationState
            {
                Position = c.P0,
                Velocity = c.V0,
                Mass = c.Mass0
            };
            var sw = Stopwatch.StartNew();
            SpacecraftIntegrationState state = start;
            double t = StudyShipT0;
            int count = checkpoints?.Length ?? 1;
            for (int k = 0; k < count; k++)
            {
                double stop = checkpoints != null ? checkpoints[k] : StudyShipT0 + c.Seconds;
                SpacecraftIntegrationResult r = dop853
                    ? SpacecraftOrbitIntegrator.StepForwardDop853(
                        state, t, stop - t, acc.Evaluate, posTol, velTol, massTol, rtol)
                    : SpacecraftOrbitIntegrator.StepForward(
                        state, t, stop - t, acc.Evaluate, posTol, velTol, massTol, rtol);
                state = r.State;
                t = stop;
                if (checkpoints != null)
                {
                    run.SamplesP[k] = state.Position;
                    run.SamplesV[k] = state.Velocity;
                }
            }

            sw.Stop();
            run.P = state.Position;
            run.V = state.Velocity;
            run.M = state.Mass;
            run.Calls = acc.Calls;
            run.Ms = sw.Elapsed.TotalMilliseconds;
            return run;
        });
    }

    private static StudyShipRun StudyShipMatrixWorld(
        StudyShipMatrixCase c, bool dop853,
        double posTol = 1e-4d, double velTol = 1e-8d, double massTol = 1e-4d,
        double rtol = 1e-11d, double chunkSeconds = 600d, bool useToleranceFrame = true,
        bool splitThrust = false, double[] checkpoints = null)
    {
        return StudyShipMatrixRepeat(() =>
        {
            OrbitingBody central = c.Sys.AllBodies[c.Central];
            c.Sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
            var counting = new CountingSource(new CachedGravitySource(c.Sys));
            var phys = new SpacecraftPhysics
            {
                Integrator = dop853 ? SpacecraftIntegrator.Dop853 : SpacecraftIntegrator.Dopri5,
                ToleranceFrameSystem = useToleranceFrame ? c.Sys : null,
                PositionAbsoluteTolerance = posTol,
                VelocityAbsoluteTolerance = velTol,
                MassAbsoluteTolerance = massTol,
                RelativeTolerance = rtol
            };
            phys.Sources.Add(counting);
            if (c.Drag != null)
            {
                phys.Sources.Add(c.Drag);
            }

            if (c.Thrust)
            {
                phys.Sources.Add(new ThrustSource(c.ThrustNewtons, 0d, new ConstantIsp(c.ThrustIsp))
                {
                    ThrottleAt = t => StudyShipMatrixThrottle(c, t),
                    ThrustDirection = c.ThrustDirection
                });
            }

            var run = new StudyShipRun
            {
                SamplesP = checkpoints != null ? new Vector3d[checkpoints.Length] : null,
                SamplesV = checkpoints != null ? new Vector3d[checkpoints.Length] : null,
                Steps = -1L
            };
            var ship = new Spacecraft(bp + c.P0, bv + c.V0, c.Mass0);
            var sw = Stopwatch.StartNew();
            double t = StudyShipT0;
            double endTime = StudyShipT0 + c.Seconds;
            double[] edges = splitThrust
                ? StudyShipRefBreakpoints(c)
                : new[] { t, endTime };
            int sample = 0;
            int sampleCount = checkpoints?.Length ?? 0;

            void RecordSample(double time)
            {
                // The production local path already carries the state relative to
                // the dominant body. Re-deriving it as (absolute - body) adds
                // ulp(1.5e11) to every sample and overstates production's error;
                // use the carried local state whenever the dominant body is the
                // reference's central body.
                c.Sys.EvaluateBodyState(central, time, out Vector3d bps, out Vector3d bvs);
                bool localToCentral = ship.HasLocalState && ReferenceEquals(ship.LocalBody, central);
                run.SamplesP[sample] = localToCentral
                    ? ship.LocalPosition
                    : ship.Position - bps;
                run.SamplesV[sample] = localToCentral
                    ? ship.LocalVelocity
                    : ship.Velocity - bvs;
                sample++;
            }

            void FlushSamples(double from)
            {
                while (checkpoints != null && sample < sampleCount
                    && checkpoints[sample] <= from + 1e-9d)
                {
                    RecordSample(checkpoints[sample]);
                }
            }

            void StepRange(double from, double to)
            {
                while (from < to - 1e-12d)
                {
                    FlushSamples(from);
                    double stop = to;
                    if (checkpoints != null && sample < sampleCount
                        && checkpoints[sample] > from + 1e-9d
                        && checkpoints[sample] < stop)
                    {
                        stop = checkpoints[sample];
                    }

                    double chunk = Math.Min(chunkSeconds, stop - from);
                    phys.Step(ship, from, chunk);
                    from += chunk;
                }

                FlushSamples(from);
            }

            double edge = 1e-6d;
            for (int segment = 0; segment < edges.Length - 1; segment++)
            {
                double segmentEnd = edges[segment + 1];
                if (segmentEnd - t > 3d * edge)
                {
                    StepRange(t, t + edge);
                    StepRange(t + edge, segmentEnd - edge);
                    StepRange(segmentEnd - edge, segmentEnd);
                }
                else
                {
                    StepRange(t, segmentEnd);
                }

                t = segmentEnd;
            }

            sw.Stop();
            c.Sys.EvaluateBodyState(central, StudyShipT0 + c.Seconds,
                out Vector3d bp1, out Vector3d bv1);
            bool localToCentralEnd = ship.HasLocalState && ReferenceEquals(ship.LocalBody, central);
            run.P = localToCentralEnd ? ship.LocalPosition : ship.Position - bp1;
            run.V = localToCentralEnd ? ship.LocalVelocity : ship.Velocity - bv1;
            run.M = ship.Mass;
            // The local-frame production RHS evaluates the gravity field three
            // times: the source sum (CachedGravitySource), the absolute term it
            // subtracts, and the local relative field it adds. CountingSource only
            // sees the first. Report all three gravity-field evaluations, otherwise
            // every WH/production ratio and the cost Pareto flatter production by 3x.
            run.Calls = counting.Calls * (useToleranceFrame ? 3L : 1L);
            run.Ms = sw.Elapsed.TotalMilliseconds;
            return run;
        });
    }

    private static StudyShipRun StudyShipMatrixTaylor(
        StudyShipMatrixCase c, int order, double[] checkpoints = null)
    {
        return StudyShipMatrixRepeat(() =>
        {
            var pSamples = checkpoints != null ? new List<Vector3d>(checkpoints.Length) : null;
            var vSamples = checkpoints != null ? new List<Vector3d>(checkpoints.Length) : null;
            TaylorShipResult r = TaylorShip.RunSecondsLocal(
                c.Sys, c.Central, c.P0, c.V0, StudyShipT0, c.Seconds,
                order, 1e-3d, 1e-6d, 600d, 1e-6d, checkpoints, pSamples, vSamples);
            return new StudyShipRun
            {
                P = r.P,
                V = r.V,
                M = c.Mass0,
                Calls = r.Calls,
                Steps = r.Steps,
                Ms = r.Ms,
                SamplesP = pSamples?.ToArray(),
                SamplesV = vSamples?.ToArray()
            };
        });
    }

    private static void StudyShipMatrixRk4(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls)
    {
        var k1 = new double[7];
        var k2 = new double[7];
        var k3 = new double[7];
        var k4 = new double[7];
        var tmp = new double[7];
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            acc.EvaluateArray(s, tBase, k1);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                tmp[i] = s[i] + 0.5d * h * k1[i];
            }

            acc.EvaluateArray(tmp, tBase + (0.5d * h), k2);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                tmp[i] = s[i] + 0.5d * h * k2[i];
            }

            acc.EvaluateArray(tmp, tBase + (0.5d * h), k3);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                tmp[i] = s[i] + h * k3[i];
            }

            acc.EvaluateArray(tmp, tBase + h, k4);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                s[i] += h / 6d * (k1[i] + 2d * k2[i] + 2d * k3[i] + k4[i]);
            }
        }
    }

    private static void StudyShipMatrixDopri5Fixed(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls)
    {
        double[][] A =
        {
            new[] { 1d / 5d },
            new[] { 3d / 40d, 9d / 40d },
            new[] { 44d / 45d, -56d / 15d, 32d / 9d },
            new[] { 19372d / 6561d, -25360d / 2187d, 64448d / 6561d, -212d / 729d },
            new[] { 9017d / 3168d, -355d / 33d, 46732d / 5247d, 49d / 176d, -5103d / 18656d }
        };
        double[] b = { 35d / 384d, 0d, 500d / 1113d, 125d / 192d, -2187d / 6784d, 11d / 84d };
        double[] c =
        {
            0d, DOPRI5Coefficients.c2, DOPRI5Coefficients.c3,
            DOPRI5Coefficients.c4, DOPRI5Coefficients.c5, DOPRI5Coefficients.c6
        };
        var k = new double[6][];
        for (int i = 0; i < 6; i++)
        {
            k[i] = new double[7];
        }

        var tmp = new double[7];
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            acc.EvaluateArray(s, tBase, k[0]);
            calls++;
            for (int stage = 1; stage < 6; stage++)
            {
                for (int i = 0; i < 7; i++)
                {
                    double sum = 0d;
                    for (int j = 0; j < stage; j++)
                    {
                        sum += A[stage - 1][j] * k[j][i];
                    }

                    tmp[i] = s[i] + h * sum;
                }

                acc.EvaluateArray(tmp, tBase + (c[stage] * h), k[stage]);
                calls++;
            }

            for (int i = 0; i < 7; i++)
            {
                double sum = 0d;
                for (int j = 0; j < 6; j++)
                {
                    sum += b[j] * k[j][i];
                }

                s[i] += h * sum;
            }
        }
    }

    private static void StudyShipMatrixPefrl(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls)
    {
        const double xi = 0.1786178958448091d;
        const double lam = -0.2123418310626054d;
        const double chi = -0.06626458266981849d;
        var ds = new double[7];
        acc.EvaluateArray(s, t, ds);
        calls++;
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            double dt = xi * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
            acc.EvaluateArray(s, tBase + (xi * h), ds);
            calls++;
            double kv = (1d - (2d * lam)) * h / 2d;
            s[3] += kv * ds[3];
            s[4] += kv * ds[4];
            s[5] += kv * ds[5];

            dt = chi * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
            acc.EvaluateArray(s, tBase + ((xi + chi) * h), ds);
            calls++;
            kv = lam * h;
            s[3] += kv * ds[3];
            s[4] += kv * ds[4];
            s[5] += kv * ds[5];

            dt = (1d - (2d * (chi + xi))) * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
            acc.EvaluateArray(s, tBase + ((1d - chi - xi) * h), ds);
            calls++;
            kv = lam * h;
            s[3] += kv * ds[3];
            s[4] += kv * ds[4];
            s[5] += kv * ds[5];

            dt = chi * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
            acc.EvaluateArray(s, tBase + ((1d - xi) * h), ds);
            calls++;
            kv = (1d - (2d * lam)) * h / 2d;
            s[3] += kv * ds[3];
            s[4] += kv * ds[4];
            s[5] += kv * ds[5];

            dt = xi * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
        }
    }

    private static void StudyShipMatrixY6(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls,
        bool compensated)
    {
        var ds = new double[7];
        var sl = compensated ? new double[7] : null;
        var st = new double[7];
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        Sample(s, sl, st);
        acc.EvaluateArray(st, t, ds);
        calls++;
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            for (int k = 0; k < ws.Length; k++)
            {
                double dt = ws[k] * h;
                for (int i = 3; i < 6; i++)
                {
                    Add(s, sl, i, 0.5d * dt * ds[i]);
                }

                for (int i = 0; i < 3; i++)
                {
                    Add(s, sl, i, dt * (s[i + 3] + (sl == null ? 0d : sl[i + 3])));
                }

                Add(s, sl, 6, dt * ds[6]);
                Sample(s, sl, st);
                acc.EvaluateArray(st, tBase + (cw[k + 1] * h), ds);
                calls++;
                for (int i = 3; i < 6; i++)
                {
                    Add(s, sl, i, 0.5d * dt * ds[i]);
                }
            }
        }

        if (compensated)
        {
            for (int i = 0; i < 7; i++)
            {
                s[i] += sl[i];
            }
        }
    }

    private static void Sample(double[] s, double[] sl, double[] st)
    {
        for (int i = 0; i < 7; i++)
        {
            st[i] = s[i] + (sl == null ? 0d : sl[i]);
        }
    }

    private static void Add(double[] s, double[] sl, int i, double value)
    {
        if (sl == null)
        {
            s[i] += value;
            return;
        }

        StudyAdd(ref s[i], ref sl[i], value);
    }

    /// <summary>
    /// One WH-Y6 sub-kick. `ds` holds the acceleration at the current state and
    /// time; the acceleration at the sub-step end is written to `dsNext`. For a
    /// velocity-independent force (the gravity cases WH is meant for), the end
    /// value is exactly the next sub-step's start value, so StudyShipMatrixWh
    /// reuses it and pays one RHS per sub-step instead of two (the old count
    /// overstated WH's cost by ~2x). Drag/thrust still evaluate both ends.
    /// </summary>
    private static void StudyShipMatrixWhStep(
        double[] s, double h, StudyShipMatrixAcc acc, ref long calls,
        double end, double[] ds, double[] dsNext)
    {
        double mu = acc.Case.Sys.AllBodies[acc.Case.Central]
            .ResolveStandardGravitationalParameter();
        var st = new Vector3d(s[0], s[1], s[2]);
        var sv = new Vector3d(s[3], s[4], s[5]);
        Vector3d kepler = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            st, Vector3d.Zero, mu);
        Vector3d pert = new Vector3d(ds[3], ds[4], ds[5]) - kepler;
        sv += pert * (0.5d * h);
        StudyKeplerDrift(st, sv, mu, h, out Vector3d p2, out Vector3d v2);
        s[0] = p2.X;
        s[1] = p2.Y;
        s[2] = p2.Z;
        s[3] = v2.X;
        s[4] = v2.Y;
        s[5] = v2.Z;
        s[6] += h * ds[6];
        acc.EvaluateArray(s, end, dsNext);
        calls++;
        kepler = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            p2, Vector3d.Zero, mu);
        pert = new Vector3d(dsNext[3], dsNext[4], dsNext[5]) - kepler;
        v2 += pert * (0.5d * h);
        s[3] = v2.X;
        s[4] = v2.Y;
        s[5] = v2.Z;
    }

    private static void StudyShipMatrixWh(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls)
    {
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        bool velocityIndependent = acc.Case.Drag == null && !acc.Case.Thrust;
        var ds = new double[7];
        var dsNext = new double[7];
        acc.EvaluateArray(s, t, ds);
        calls++;
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            for (int k = 0; k < ws.Length; k++)
            {
                double dt = ws[k] * h;
                double tStart = tBase + (cw[k] * h);
                double tEnd = tBase + (cw[k + 1] * h);
                if (!velocityIndependent)
                {
                    acc.EvaluateArray(s, tStart, ds);
                    calls++;
                }

                StudyShipMatrixWhStep(s, dt, acc, ref calls, tEnd, ds, dsNext);
                double[] swap = ds;
                ds = dsNext;
                dsNext = swap;
            }
        }
    }

    private static StudyShipRun StudyShipMatrixFixed(
        StudyShipMatrixCase c, string method, double h, double[] checkpoints = null)
    {
        return StudyShipMatrixRepeat(() =>
        {
            var acc = new StudyShipMatrixAcc(c);
            var s = new double[]
            {
                c.P0.X, c.P0.Y, c.P0.Z,
                c.V0.X, c.V0.Y, c.V0.Z,
                c.Mass0
            };
            var run = new StudyShipRun
            {
                SamplesP = checkpoints != null ? new Vector3d[checkpoints.Length] : null,
                SamplesV = checkpoints != null ? new Vector3d[checkpoints.Length] : null
            };
            long calls = 0L;
            long steps = 0L;
            var sw = Stopwatch.StartNew();
            double t = StudyShipT0;
            int count = checkpoints?.Length ?? 1;
            for (int k = 0; k < count; k++)
            {
                double stop = checkpoints != null ? checkpoints[k] : StudyShipT0 + c.Seconds;
                double span = stop - t;
                if (span <= 0d)
                {
                    continue;
                }

                long full = (long)Math.Floor((span / h) + 1e-12d);
                double rem = span - (full * h);
                if (full > 0L)
                {
                    StudyShipMatrixCall(method, s, t, h, full, acc, ref calls);
                    t += full * h;
                    steps += full;
                }

                if (rem > 1e-12d)
                {
                    StudyShipMatrixCall(method, s, t, rem, 1L, acc, ref calls);
                    steps++;
                }

                t = stop;
                if (checkpoints != null)
                {
                    run.SamplesP[k] = new Vector3d(s[0], s[1], s[2]);
                    run.SamplesV[k] = new Vector3d(s[3], s[4], s[5]);
                }
            }

            sw.Stop();
            run.P = new Vector3d(s[0], s[1], s[2]);
            run.V = new Vector3d(s[3], s[4], s[5]);
            run.M = s[6];
            run.Calls = calls;
            run.Steps = steps;
            run.Ms = sw.Elapsed.TotalMilliseconds;
            return run;
        });
    }

    private static void StudyShipMatrixCall(
        string method, double[] s, double t, double h, long steps,
        StudyShipMatrixAcc acc, ref long calls)
    {
        switch (method)
        {
            case "RK4":
                StudyShipMatrixRk4(s, t, h, steps, acc, ref calls);
                break;
            case "DOPRI5":
                StudyShipMatrixDopri5Fixed(s, t, h, steps, acc, ref calls);
                break;
            case "PEFRL":
                StudyShipMatrixPefrl(s, t, h, steps, acc, ref calls);
                break;
            case "Y6":
                StudyShipMatrixY6(s, t, h, steps, acc, ref calls, false);
                break;
            case "Y6C":
                StudyShipMatrixY6(s, t, h, steps, acc, ref calls, true);
                break;
            case "WH-Y6":
                StudyShipMatrixWh(s, t, h, steps, acc, ref calls);
                break;
            default:
                throw new ArgumentException("unknown method " + method);
        }
    }

    private static double StudyShipMatrixEnergy(Vector3d p, Vector3d v, double mu)
    {
        return 0.5d * v.SqrMagnitude - mu / p.Magnitude;
    }

    private static double StudyShipMaxSample(Vector3d[] samples, Vector3d[] reference)
    {
        if (samples == null || reference == null)
        {
            return double.NaN;
        }

        // A shorter sample list must not be silently truncated: a method that lost
        // a checkpoint (Taylor's sampled run) would otherwise look accurate on the
        // prefix it did produce. Mismatch is a data error, not a missing tail.
        if (samples.Length != reference.Length || samples.Length == 0)
        {
            return double.NaN;
        }

        double worst = 0d;
        for (int i = 0; i < samples.Length; i++)
        {
            double d = (samples[i] - reference[i]).Magnitude;
            if (d > worst)
            {
                worst = d;
            }
        }

        return worst;
    }

    /// <summary>
    /// Worst separation of two rows over the whole trajectory, not just the
    /// endpoint: two integrators can agree at the end after diverging in between.
    /// </summary>
    private static double StudyShipRowSeparation(
        StudyShipMatrixRow a, StudyShipMatrixRow b)
    {
        if (a == null || b == null)
        {
            return double.NaN;
        }

        double worst = (a.P - b.P).Magnitude;
        if (a.SamplesP != null && b.SamplesP != null)
        {
            if (a.SamplesP.Length != b.SamplesP.Length)
            {
                return double.NaN;
            }

            for (int i = 0; i < a.SamplesP.Length; i++)
            {
                double d = (a.SamplesP[i] - b.SamplesP[i]).Magnitude;
                if (d > worst)
                {
                    worst = d;
                }
            }
        }

        return worst;
    }

    private static StudyShipMatrixRow StudyShipMatrixRowMake(
        string method, string config, string frame,
        StudyShipMatrixCase c, StudyShipMatrixRefs refs,
        StudyShipRun run, string note)
    {
        var row = new StudyShipMatrixRow
        {
            Method = method,
            Config = config,
            Frame = frame,
            Calls = run.Calls,
            // TaylorShip.Calls counts jet-derivative orders (one full K12 jet is
            // 12 calls), so a raw call comparison flatters it by ~12x before the
            // per-call cost difference is even counted.
            Cost = method == "Taylor" ? run.Calls * 12L : run.Calls,
            Steps = run.Steps,
            Ms = run.Ms,
            Reps = run.Reps,
            Note = note,
            P = run.P,
            V = run.V,
            SamplesP = run.SamplesP,
            // Actual samples produced by this row, not the reference's checkpoint
            // count: a lost sample is visible in the CSV and fails the max metric.
            SampleCount = run.SamplesP?.Length ?? 0
        };
        bool finite = run.P.IsFinite && run.V.IsFinite
            && double.IsFinite(run.M) && run.M > 0d;
        row.Finite = finite;
        if (!finite)
        {
            row.Dp = double.NaN;
            row.DpFinal = double.NaN;
            row.Dv = double.NaN;
            row.DvFinal = double.NaN;
            row.Da = double.NaN;
            row.De = double.NaN;
            row.DEnergy = double.NaN;
            return row;
        }

        StudyShipMatrixRef reference = refs.Local;
        row.DpFinal = (run.P - reference.P).Magnitude;
        row.DvFinal = (run.V - reference.V).Magnitude;
        double maxP = StudyShipMaxSample(run.SamplesP, refs.CheckpointP);
        double maxV = StudyShipMaxSample(run.SamplesV, refs.CheckpointV);
        // The ranked metric is the worst point of the whole trajectory: a final
        // point alone rewards a lucky phase on long runs.
        row.Dp = double.IsFinite(maxP) ? Math.Max(maxP, row.DpFinal) : row.DpFinal;
        row.Dv = double.IsFinite(maxV) ? Math.Max(maxV, row.DvFinal) : row.DvFinal;
        OrbitalElements el = OrbitalElements.FromState(
            run.P, run.V, c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter());
        row.Da = el.IsValid && reference.Elements.IsValid
            ? Math.Abs(el.SemiMajorAxis - reference.Elements.SemiMajorAxis)
            : double.NaN;
        row.De = el.IsValid && reference.Elements.IsValid
            ? Math.Abs(el.Eccentricity - reference.Elements.Eccentricity)
            : double.NaN;
        double energy = StudyShipMatrixEnergy(run.P, run.V,
            c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter());
        double refEnergy = StudyShipMatrixEnergy(reference.P, reference.V,
            c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter());
        row.DEnergy = Math.Abs(energy - refEnergy) / Math.Max(Math.Abs(refEnergy), 1e-300d);
        return row;
    }

    private static void StudyShipMatrixRunAll(
        StudyShipMatrixCase c, StudyShipMatrixRefs refs, List<StudyShipMatrixRow> rows)
    {
        double[] checkpoints = refs.CheckpointTimes;
        void Add(string method, string config, string frame, Func<StudyShipRun> run, string note)
        {
            var rowWatch = Stopwatch.StartNew();
            try
            {
                rows.Add(StudyShipMatrixRowMake(
                    method, config, frame, c, refs, run(), note));
            }
            catch (Exception ex)
            {
                rows.Add(StudyShipMatrixRowMake(
                    method, config, frame, c, refs,
                    new StudyShipRun { P = Vector3d.Zero, V = Vector3d.Zero, M = 0d },
                    StudyShipMatrixNote(ex)));
            }

            rowWatch.Stop();
            Console.WriteLine("  row " + frame + " " + method + " " + config
                + ": " + rowWatch.ElapsedMilliseconds + " ms");
        }

        string H(double h)
        {
            return "h=" + h.ToString("G6", CultureInfo.InvariantCulture);
        }

        double[] grid = StudyShipMatrixSteps(c);
        foreach (double h in grid)
        {
            Add("RK4", H(h), "local",
                () => StudyShipMatrixFixed(c, "RK4", h, checkpoints), "");
        }

        foreach (string method in new[] { "DOPRI5", "PEFRL" })
        {
            foreach (double h in grid)
            {
                Add(method, H(h), "local",
                    () => StudyShipMatrixFixed(c, method, h, checkpoints), "");
            }
        }

        foreach (string method in new[] { "Y6", "Y6C", "WH-Y6" })
        {
            foreach (double h in grid)
            {
                Add(method, H(h), "local",
                    () => StudyShipMatrixFixed(c, method, h, checkpoints), "");
            }
        }

        foreach (bool dop853 in new[] { true, false })
        {
            foreach (double tol in new[] { 1e-4d, 1e-6d, 1e-8d })
            {
                string cfg = "tol=" + tol.ToString("G3", CultureInfo.InvariantCulture);
                Add(dop853 ? "DOP853" : "DOPRI5", cfg, "local",
                    () => StudyShipMatrixAdaptive(
                        c, dop853, tol, tol * 1e-3d, tol, 1e-12d, checkpoints),
                    "");
            }
        }

        string worldCfg = "world " + c.WorldChunkSeconds.ToString("G", CultureInfo.InvariantCulture) + " s";
        foreach (bool dop853 in new[] { true, false })
        {
            Add(dop853 ? "DOP853" : "DOPRI5", worldCfg, "world",
                () => StudyShipMatrixWorld(
                    c, dop853, chunkSeconds: c.WorldChunkSeconds, checkpoints: checkpoints),
                "production");
        }

        if (c.Thrust)
        {
            Add("DOP853", "world burn-split", "world",
                () => StudyShipMatrixWorld(
                    c, true, chunkSeconds: c.WorldChunkSeconds, splitThrust: true,
                    checkpoints: checkpoints),
                "burn-split");
        }

        if (c.Thrust && Math.Abs(c.WorldChunkSeconds - 600d) > 1e-9d)
        {
            foreach (bool dop853 in new[] { true, false })
            {
                Add(dop853 ? "DOP853" : "DOPRI5", "world 600 s (fixed)", "world",
                    () => StudyShipMatrixWorld(
                        c, dop853, chunkSeconds: 600d, checkpoints: checkpoints),
                    "trial-mass check");
            }
        }

        if (c.AllowTaylor)
        {
            foreach (int order in new[] { 12 })
            {
                string cfg = "K" + order.ToString(CultureInfo.InvariantCulture);
                Add("Taylor", cfg, "local",
                    () => StudyShipMatrixTaylor(c, order, checkpoints), "");
            }
        }
    }

    private static void StudyShipMatrixPrint(
        StudyShipMatrixCase c, StudyShipMatrixRefs refs, List<StudyShipMatrixRow> rows)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + c.Label + " (" + (c.Seconds / 86400d).ToString("G6", CultureInfo.InvariantCulture) + " d) ===");
        Console.WriteLine("reference: " + refs.LocalKind
            + ((refs.LocalAsymptotic || refs.LocalResolved) ? "" : " [INVALID]") + ", est "
            + refs.LocalError.ToString("E2", CultureInfo.InvariantCulture) + " m"
            + ", resolution=" + refs.LocalFinestDelta.ToString("E2", CultureInfo.InvariantCulture) + " m"
            + ", ratio=" + refs.LocalRatio.ToString("F2", CultureInfo.InvariantCulture));
        Console.WriteLine("reference target: " + c.AccuracyTarget.ToString("G3", CultureInfo.InvariantCulture)
            + " m (alternative methods " + c.AlternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m)");
        if (refs.HasFormulation)
        {
            Console.WriteLine("cross-check DD: " + refs.DdKind + ", self="
                + refs.DdSelf.ToString("E3", CultureInfo.InvariantCulture) + " m");
            Console.WriteLine("certified-vs-DD: "
                + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture) + " m");
        }
        else
        {
            Console.WriteLine("certified-vs-DD: N/A (perturbed/thrust)");
        }
        double floor = refs.ReferenceFloor;
        Console.WriteLine("ranking floor: "
            + (double.IsFinite(floor)
                ? floor.ToString("E2", CultureInfo.InvariantCulture)
                  + " m (a row below it is measuring reference noise, not accuracy)"
                : "none (reference not certified; no accuracy ranking)")
            + "; " + refs.CheckpointTimes.Length + " trajectory checkpoints");
        if (refs.HasFormulation && !c.UseTaylorDdReference && double.IsFinite(floor)
            && refs.Formulation > 10d * floor)
        {
            Console.WriteLine("INFO formulation difference certified-local vs local-DD = "
                + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture)
                + " m, above the reference's own "
                + floor.ToString("E2", CultureInfo.InvariantCulture)
                + " m convergence; informational sensitivity, not an error gate");
        }
        Console.WriteLine("frame method      config          dp_max(m)   dp_final(m) dv_max(m/s) da(m)       de          dE/E        calls      steps      ms      reps");
        Console.WriteLine("calls are RHS evaluations for fixed/adaptive rows; Taylor calls are jet-derivative orders (cost = calls x 12). ms is sequential min-of-up-to-3.");
        foreach (StudyShipMatrixRow r in rows)
        {
            string note = r.Note ?? "";
            if (double.IsFinite(floor) && double.IsFinite(r.Dp) && r.Dp < floor)
            {
                note = (note.Length > 0 ? note + " " : "") + "at-floor";
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-5} {1,-11} {2,-15} {3,11:E3} {4,11:E3} {5,11:E3} {6,11:E3} {7,11:E3} {8,11:E3} {9,10} {10,10} {11,8:F0} {12,3}{13}",
                r.Frame, r.Method, r.Config, r.Dp, r.DpFinal, r.Dv, r.Da, r.De,
                r.DEnergy,
                r.Calls.ToString("N0", CultureInfo.InvariantCulture),
                r.Steps < 0L ? "-" : r.Steps.ToString("N0", CultureInfo.InvariantCulture),
                r.Ms, r.Reps, note.Length > 0 ? " " + note : ""));
        }

        if (double.IsFinite(floor))
        {
            // Ranking below the formulation floor would be noise, so raise the
            // Pareto target to the floor instead of producing an empty set.
            double target = Math.Max(c.AccuracyTarget, floor);
            StudyShipMatrixPareto(rows, "local", target, floor);
            StudyShipMatrixPareto(rows, "world", target, floor);
        }
        else
        {
            Console.WriteLine("no Pareto ranking: the reference is not certified, so no row can be ranked by accuracy");
        }
    }

    private static double StudyShipMatrixRanked(StudyShipMatrixRow r, double floor)
    {
        if (!double.IsFinite(r.Dp))
        {
            return double.NaN;
        }

        return double.IsFinite(floor) ? Math.Max(r.Dp, floor) : r.Dp;
    }

    private static void StudyShipMatrixPareto(
        List<StudyShipMatrixRow> rows, string frame, double target, double floor)
    {
        var valid = new List<StudyShipMatrixRow>();
        foreach (StudyShipMatrixRow r in rows)
        {
            double ranked = StudyShipMatrixRanked(r, floor);
            // Taylor's "calls" are jet-derivative orders, not RHS evaluations; the
            // x12 cost is a rough proxy and not calibrated against the measured
            // per-call work. Keep its row in the table, but do not let an
            // uncalibrated cost model decide the production Pareto.
            if (r.Method != "Taylor" && r.Frame == frame && r.Finite
                && double.IsFinite(ranked) && r.Cost > 0L && ranked <= target)
            {
                valid.Add(r);
            }
        }

        if (valid.Count == 0)
        {
            Console.WriteLine("no valid rows");
            return;
        }

        Console.Write(frame + " Pareto (RHS-eq cost, dp):");
        foreach (StudyShipMatrixRow r in valid)
        {
            double rp = StudyShipMatrixRanked(r, floor);
            bool dominated = false;
            foreach (StudyShipMatrixRow q in valid)
            {
                if (q == r)
                {
                    continue;
                }

                double qp = StudyShipMatrixRanked(q, floor);
                if (q.Cost <= r.Cost && qp <= rp
                    && (q.Cost < r.Cost || qp < rp))
                {
                    dominated = true;
                    break;
                }
            }

            if (!dominated)
            {
                Console.Write(" " + r.Method + "/" + r.Config
                    + "=" + r.Dp.ToString("E2", CultureInfo.InvariantCulture)
                    + (rp > r.Dp ? "(floor)" : "")
                    + "@" + r.Cost.ToString("N0", CultureInfo.InvariantCulture)
                    + (r.Cost != r.Calls
                        ? " (" + r.Calls.ToString("N0", CultureInfo.InvariantCulture) + " calls)"
                        : "")
                    + "/" + r.Ms.ToString("F1", CultureInfo.InvariantCulture) + "ms");
            }
        }

        Console.WriteLine();
        StudyShipMatrixRow accurate = valid[0];
        foreach (StudyShipMatrixRow r in valid)
        {
            double rp = StudyShipMatrixRanked(r, floor);
            double ap = StudyShipMatrixRanked(accurate, floor);
            if (rp < ap || (rp == ap && r.Cost < accurate.Cost))
            {
                accurate = r;
            }
        }

        double accurateRanked = StudyShipMatrixRanked(accurate, floor);
        Console.WriteLine("  best accuracy: " + accurate.Method + "/" + accurate.Config
            + " dp=" + accurate.Dp.ToString("E2", CultureInfo.InvariantCulture)
            + (accurateRanked > accurate.Dp ? " (at reference floor)" : "")
            + " m cost=" + accurate.Cost.ToString("N0", CultureInfo.InvariantCulture)
            + " calls=" + accurate.Calls.ToString("N0", CultureInfo.InvariantCulture)
            + " ms=" + accurate.Ms.ToString("F0", CultureInfo.InvariantCulture));
        foreach (double limit in new[] { 1d, 10d, 100d, 1000d })
        {
            StudyShipMatrixRow bestCost = null;
            StudyShipMatrixRow bestMs = null;
            foreach (StudyShipMatrixRow r in valid)
            {
                double rp = StudyShipMatrixRanked(r, floor);
                if (rp > limit)
                {
                    continue;
                }

                if (bestCost == null || r.Cost < bestCost.Cost
                    || (r.Cost == bestCost.Cost
                        && rp < StudyShipMatrixRanked(bestCost, floor)))
                {
                    bestCost = r;
                }

                if (bestMs == null || r.Ms < bestMs.Ms
                    || (r.Ms == bestMs.Ms
                        && rp < StudyShipMatrixRanked(bestMs, floor)))
                {
                    bestMs = r;
                }
            }

            Console.WriteLine("  <= " + limit.ToString("G", CultureInfo.InvariantCulture) + " m: RHS-eq cost "
                + (bestCost == null ? "none"
                    : bestCost.Method + "/" + bestCost.Config + " cost="
                      + bestCost.Cost.ToString("N0", CultureInfo.InvariantCulture)
                      + " calls=" + bestCost.Calls.ToString("N0", CultureInfo.InvariantCulture)
                      + " dp=" + bestCost.Dp.ToString("E2", CultureInfo.InvariantCulture))
                + "; ms "
                + (bestMs == null ? "none"
                    : bestMs.Method + "/" + bestMs.Config + " ms="
                      + bestMs.Ms.ToString("F0", CultureInfo.InvariantCulture)
                      + " dp=" + bestMs.Dp.ToString("E2", CultureInfo.InvariantCulture)));
        }
    }

    private static void StudyShipMatrixScoreboard(
        List<StudyShipMatrixRow> all, List<string> labels,
        Dictionary<string, double> floors)
    {
        foreach (string frame in new[] { "local", "world" })
        {
            foreach (double target in new[] { 1d, 10d, 100d })
            {
                var winsCost = new Dictionary<string, int>();
                var winsMs = new Dictionary<string, int>();
                var seen = new HashSet<string>();
                for (int i = 0; i < all.Count; i++)
                {
                    string scenario = labels[i];
                    if (!seen.Add(scenario))
                    {
                        continue;
                    }

                    floors.TryGetValue(scenario, out double floor);
                    double scenarioTarget = Math.Max(
                        target, double.IsFinite(floor) ? floor : target);
                    StudyShipMatrixRow bestCost = null;
                    StudyShipMatrixRow bestMs = null;
                    for (int j = 0; j < all.Count; j++)
                    {
                        StudyShipMatrixRow q = all[j];
                        double qp = StudyShipMatrixRanked(q, floor);
                        if (labels[j] != scenario || q.Frame != frame || !q.Finite
                            || !double.IsFinite(qp) || q.Cost <= 0L || qp > scenarioTarget)
                        {
                            continue;
                        }

                        if (q.Method != "Taylor"
                            && (bestCost == null || q.Cost < bestCost.Cost
                                || (q.Cost == bestCost.Cost
                                    && qp < StudyShipMatrixRanked(bestCost, floor))))
                        {
                            bestCost = q;
                        }

                        if (bestMs == null || q.Ms < bestMs.Ms
                            || (q.Ms == bestMs.Ms
                                && qp < StudyShipMatrixRanked(bestMs, floor)))
                        {
                            bestMs = q;
                        }
                    }

                    if (bestCost != null)
                    {
                        string key = bestCost.Method + "/" + bestCost.Config;
                        winsCost[key] = winsCost.TryGetValue(key, out int n) ? n + 1 : 1;
                    }

                    if (bestMs != null)
                    {
                        string key = bestMs.Method + "/" + bestMs.Config;
                        winsMs[key] = winsMs.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                }

                Console.Write(frame + " <= " + target.ToString("G", CultureInfo.InvariantCulture)
                    + " m wins by RHS-eq cost:");
                StudyShipMatrixPrintWins(winsCost);
                Console.Write(frame + " <= " + target.ToString("G", CultureInfo.InvariantCulture)
                    + " m wins by ms (min of up to 3):");
                StudyShipMatrixPrintWins(winsMs);
            }
        }
    }

    private static void StudyShipMatrixPrintWins(Dictionary<string, int> wins)
    {
        if (wins.Count == 0)
        {
            Console.Write(" none");
        }
        else
        {
            foreach (var kv in wins)
            {
                Console.Write(" " + kv.Key + "=" + kv.Value);
            }
        }

        Console.WriteLine();
    }

    private static string StudyCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string StudyCsvNumber(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void StudyShipMatrixCsv(
        List<StudyShipMatrixRow> all, List<string> labels,
        Dictionary<string, double> floors, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario,frame,method,config,dp_max_m,dp_final_m,dv_max_mps,"
            + "da_m,de,denergy,calls,cost_rhs_eq,steps,ms,reps,samples,ref_err_m,dp_ranked_m,finite,note");
        for (int i = 0; i < all.Count; i++)
        {
            StudyShipMatrixRow r = all[i];
            floors.TryGetValue(labels[i], out double floor);
            double ranked = StudyShipMatrixRanked(r, floor);
            sb.Append(StudyCsvField(labels[i])).Append(',')
                .Append(StudyCsvField(r.Frame)).Append(',')
                .Append(StudyCsvField(r.Method)).Append(',')
                .Append(StudyCsvField(r.Config)).Append(',')
                .Append(StudyCsvNumber(r.Dp)).Append(',')
                .Append(StudyCsvNumber(r.DpFinal)).Append(',')
                .Append(StudyCsvNumber(r.Dv)).Append(',')
                .Append(StudyCsvNumber(r.Da)).Append(',')
                .Append(StudyCsvNumber(r.De)).Append(',')
                .Append(StudyCsvNumber(r.DEnergy)).Append(',')
                .Append(r.Calls.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Cost.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Steps.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(StudyCsvNumber(r.Ms)).Append(',')
                .Append(r.Reps.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.SampleCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(StudyCsvNumber(double.IsFinite(floor) ? floor : double.NaN)).Append(',')
                .Append(StudyCsvNumber(ranked)).Append(',')
                .Append(r.Finite ? "1" : "0").Append(',')
                .Append(StudyCsvField(r.Note)).AppendLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("csv: " + path);
    }

    private static (Vector3d p, Vector3d v) StudyShipMatrixAnalyticCircle(
        double radius, double speed, double seconds)
    {
        double n = speed / radius;
        double theta = n * seconds;
        double c = Math.Cos(theta);
        double s = Math.Sin(theta);
        return (
            new Vector3d(radius * c, radius * s, 0d),
            new Vector3d(-speed * s, speed * c, 0d));
    }

    private static void StudyShipMatrixOrderAndTolerance()
    {
        Console.WriteLine();
        Console.WriteLine("=== Pure two-body: fixed-step convergence order and tolerance scaling ===");
        const double mu = 3.986004418e14d;
        const double radius = 6.371e6d + 4e5d;
        var body = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = mu,
            Radius = 6.371e6d
        };
        var sys = new StarSystem(body);
        double speed = Math.Sqrt(mu / radius);
        double period = 2d * Math.PI * Math.Sqrt(radius * radius * radius / mu);
        var c = new StudyShipMatrixCase
        {
            Label = "pure two-body",
            Sys = sys,
            Central = 0,
            P0 = new Vector3d(radius, 0d, 0d),
            V0 = new Vector3d(0d, speed, 0d),
            Mass0 = 1000d,
            Seconds = 10d * period,
            Tau = period,
            StepScale = 1d
        };
        var exact = StudyShipMatrixAnalyticCircle(radius, speed, c.Seconds);

        var methods = new[]
        {
            ("RK4", 64d, 3.5d, 4.5d),
            ("PEFRL", 64d, 3.5d, 4.5d),
            ("DOPRI5", 64d, 4.5d, 5.5d),
            ("Y6", 16d, 5.5d, 6.5d)
        };
        foreach (var entry in methods)
        {
            string method = entry.Item1;
            double h = period / entry.Item2;
            var e1 = StudyShipMatrixFixed(c, method, h);
            var e2 = StudyShipMatrixFixed(c, method, h * 0.5d);
            var e3 = StudyShipMatrixFixed(c, method, h * 0.25d);
            double err1 = (e1.P - exact.p).Magnitude;
            double err2 = (e2.P - exact.p).Magnitude;
            double err3 = (e3.P - exact.p).Magnitude;
            double order = Math.Log(err1 / Math.Max(err2, 1e-30d), 2d);
            double order2 = Math.Log(err2 / Math.Max(err3, 1e-30d), 2d);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-8} h={1,8:F3} err={2,11:E3}/{3,11:E3}/{4,11:E3} order={5:F2}/{6:F2}",
                method, h, err1, err2, err3, order, order2));
            Check(order > entry.Item3 - 0.5d && order < entry.Item4 + 0.5d,
                "order " + method, order.ToString("F2", CultureInfo.InvariantCulture));
            Check(order2 > entry.Item3 - 0.5d && order2 < entry.Item4 + 0.5d,
                "order2 " + method, order2.ToString("F2", CultureInfo.InvariantCulture));
        }

        var wh = StudyShipMatrixFixed(c, "WH-Y6", period / 16d);
        double whErr = (wh.P - exact.p).Magnitude;
        Console.WriteLine("WH-Y6 pure Kepler error: " + whErr.ToString("E3", CultureInfo.InvariantCulture) + " m");
        Check(whErr < 1e-3d, "WH-Y6 pure Kepler exact drift",
            whErr.ToString("E3", CultureInfo.InvariantCulture));

        double[] dopDiv = { 4d, 8d, 16d, 32d, 64d };
        var dopErr = new double[dopDiv.Length];
        for (int i = 0; i < dopDiv.Length; i++)
        {
            SpacecraftIntegrationState r = StudyShipRefDop853Fixed(
                c, period / dopDiv[i], out _);
            dopErr[i] = (r.Position - exact.p).Magnitude;
        }

        Console.Write("DOP853 fixed-step err:");
        for (int i = 0; i < dopErr.Length; i++)
        {
            Console.Write(" " + dopErr[i].ToString("E2", CultureInfo.InvariantCulture));
        }

        Console.WriteLine();
        int last = dopErr.Length - 1;
        double lastOrder = Math.Log(
            Math.Max(dopErr[last - 1], 1e-300d) / Math.Max(dopErr[last], 1e-300d), 2d);
        Check(lastOrder > 6.5d && lastOrder < 9.5d,
            "DOP853 fixed order (finest pair)",
            lastOrder.ToString("F2", CultureInfo.InvariantCulture));
        Check(dopErr[last] < 1e-2d,
            "DOP853 fixed accuracy (finest pair)",
            dopErr[last].ToString("E2", CultureInfo.InvariantCulture) + " m < 1E-02 m");

        StudyShipMatrixWhEccentric();

        var reverseAcc = new StudyShipMatrixAcc(c);
        var reverseState = new double[]
        {
            c.P0.X, c.P0.Y, c.P0.Z,
            c.V0.X, c.V0.Y, c.V0.Z,
            c.Mass0
        };
        long reverseCalls = 0L;
        StudyShipMatrixCall("Y6", reverseState, StudyShipT0, period / 16d, 160L, reverseAcc, ref reverseCalls);
        StudyShipMatrixCall("Y6", reverseState, StudyShipT0 + c.Seconds, -period / 16d, 160L, reverseAcc, ref reverseCalls);
        double reverseError = Math.Sqrt(
            ((reverseState[0] - c.P0.X) * (reverseState[0] - c.P0.X))
            + ((reverseState[1] - c.P0.Y) * (reverseState[1] - c.P0.Y))
            + ((reverseState[2] - c.P0.Z) * (reverseState[2] - c.P0.Z)));
        Console.WriteLine("Y6 time-reversal error: " + reverseError.ToString("E3", CultureInfo.InvariantCulture) + " m");
        Check(reverseError < 1e-6d, "Y6 time reversibility",
            reverseError.ToString("E3", CultureInfo.InvariantCulture));

        // Absolute-tolerance sweep must NOT carry a fixed relative term: with
        // rtol=1e-12 and |r|=6.8e6 m the relative part is 6.8e-6 m and completely
        // hides tol=1e-6 vs 1e-9 (the old test compared the same run to itself).
        double[] tolerances = { 1e-1d, 1e-2d, 1e-3d, 1e-4d };
        var errors = new double[tolerances.Length];
        Console.WriteLine("DOP853 absolute tolerance sweep (rtol=0), floor from 16*eps*|r| = "
            + (16d * 2.220446049250313e-16 * radius).ToString("E2", CultureInfo.InvariantCulture) + " m");
        for (int i = 0; i < tolerances.Length; i++)
        {
            double tol = tolerances[i];
            var r = StudyShipMatrixAdaptive(c, true, tol, tol * 1e-3d, tol, 0d);
            errors[i] = (r.P - exact.p).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "DOP853 tol={0:E1} rtol=0 err={1:E3} m calls={2:N0}",
                tol, errors[i], r.Calls));
            Check(errors[i] < 10d * tol,
                "tolerance bound " + tol.ToString("E1", CultureInfo.InvariantCulture),
                errors[i].ToString("E3", CultureInfo.InvariantCulture) + " m < 10x tol");
        }

        for (int i = 1; i < errors.Length; i++)
        {
            Check(errors[i] <= errors[i - 1] * 1.05d,
                "tolerance monotonic " + tolerances[i].ToString("E0", CultureInfo.InvariantCulture),
                errors[i].ToString("E3", CultureInfo.InvariantCulture) + " <= "
                + errors[i - 1].ToString("E3", CultureInfo.InvariantCulture));
        }

        double improvement = errors[0] / Math.Max(errors[2], 1e-30d);
        Check(improvement > 10d,
            "tolerance controls error",
            "err(tol=1e-1)/err(tol=1e-3) = "
            + improvement.ToString("F1", CultureInfo.InvariantCulture) + " > 10");

        StudyShipMatrixJ2Secular();
        StudyShipMatrixDragThrustForce();
    }

    private static void StudyShipMatrixDragThrustForce()
    {
        Console.WriteLine();
        Console.WriteLine("=== Drag/thrust force formula (independent recompute) ===");
        var earth = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = 3.986004418e14d,
            Radius = 6.371e6d,
            RotationPeriodSeconds = 86164.0905d,
            Atmosphere = new AtmosphereProfile
            {
                TopAltitudeMeters = 2e6d,
                SeaLevelDensityKgPerCubicMeter = 1.225d,
                ScaleHeightMeters = 8500d
            }
        };
        var sys = new StarSystem(earth);
        var drag = new DragSource(earth) { DragCoefficient = 2.2d, ReferenceAreaM2 = 12d };
        earth.EvaluateWorldState(StudyShipT0, out Vector3d bp, out Vector3d bv);
        Vector3d relP = new Vector3d(earth.Radius + 250e3d, 0d, 0d);
        Vector3d shipV = bv + new Vector3d(0d, 7700d, 0d);
        DynamicsContribution dc = drag.Evaluate(bp + relP, shipV, 1000d, StudyShipT0);
        double altitude = relP.Magnitude - earth.Radius;
        double density = 1.225d * Math.Exp(-altitude / 8500d);
        Vector3d atmosphereV = bv + Vector3d.Cross(
            earth.SpinAxis * earth.SpinAngularSpeed, relP);
        Vector3d vRel = shipV - atmosphereV;
        Vector3d expected = vRel * (-0.5d * density * vRel.SqrMagnitude
            * 2.2d * 12d / Math.Max(vRel.Magnitude, 1e-30d));
        double dragRel = (dc.Force - expected).Magnitude / Math.Max(expected.Magnitude, 1e-30d);
        Check(dragRel < 1e-12d, "drag force formula",
            dragRel.ToString("E2", CultureInfo.InvariantCulture) + " relative");

        var thrust = new ThrustSource(2000d, 0d, new ConstantIsp(300d))
        {
            ThrottleAt = _ => 1d,
            ThrustDirection = new Vector3d(0d, 1d, 0d)
        };
        DynamicsContribution tc = thrust.Evaluate(Vector3d.Zero, Vector3d.Zero, 1000d, StudyShipT0);
        double flowError = Math.Abs(tc.MassFlow - (2000d / (300d * 9.80665d)));
        Check((tc.Force - new Vector3d(0d, 2000d, 0d)).Magnitude < 1e-9d && flowError < 1e-12d,
            "thrust force formula",
            "force " + (tc.Force - new Vector3d(0d, 2000d, 0d)).Magnitude.ToString("E2", CultureInfo.InvariantCulture)
            + " N, flow " + flowError.ToString("E2", CultureInfo.InvariantCulture) + " kg/s");
    }

    private static void StudyShipMatrixJ2Secular()
    {
        Console.WriteLine();
        Console.WriteLine("=== J2 secular nodal regression vs first-order analytic ===");
        const double mu = 3.986004418e14d;
        const double radius = 6.371e6d;
        const double j2 = 1.08263e-3d;
        const double rp = radius + 4e5d;
        double inc = 53d * Math.PI / 180d;
        var body = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = mu,
            Radius = radius,
            J2 = j2,
            J2ReferenceRadius = radius,
            NorthPoleDirection = new Vector3d(0d, 0d, 1d)
        };
        var sys = new StarSystem(body);
        double speed = Math.Sqrt(mu / rp);
        var p0 = new Vector3d(rp, 0d, 0d);
        var v0 = new Vector3d(0d, speed * Math.Cos(inc), speed * Math.Sin(inc));
        Vector3d h0 = Vector3d.Cross(p0, v0);
        double node0 = Math.Atan2(h0.X, -h0.Y);
        double period = 2d * Math.PI * Math.Sqrt(rp * rp * rp / mu);
        int orbits = 100;
        var c = new StudyShipMatrixCase
        {
            Label = "J2 secular",
            Sys = sys,
            Central = 0,
            P0 = p0,
            V0 = v0,
            Mass0 = 1000d,
            Seconds = orbits * period,
            Tau = period,
            StepScale = 1d
        };
        SpacecraftIntegrationState r = StudyShipRefDop853Fixed(c, period / 64d, out _);
        Vector3d h1 = Vector3d.Cross(r.Position, r.Velocity);
        double node1 = Math.Atan2(h1.X, -h1.Y);
        double dNode = node1 - node0;
        while (dNode > Math.PI)
        {
            dNode -= 2d * Math.PI;
        }

        while (dNode < -Math.PI)
        {
            dNode += 2d * Math.PI;
        }

        double n = Math.Sqrt(mu / (rp * rp * rp));
        double nodeDot = -1.5d * n * j2 * (radius / rp) * (radius / rp) * Math.Cos(inc);
        double expected = nodeDot * c.Seconds;
        double relative = Math.Abs(dNode - expected) / Math.Abs(expected);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "dNode={0:E4} rad, analytic={1:E4} rad over {2} orbits, relative={3:P2}",
            dNode, expected, orbits, relative));
        Check(relative < 0.02d,
            "J2 nodal regression",
            relative.ToString("P2", CultureInfo.InvariantCulture) + " < 2%");
    }

    private static void StudyShipMatrixWhEccentric()
    {
        Console.WriteLine();
        Console.WriteLine("=== WH-Y6 eccentric Kepler vs Kepler3DMatrix gold ===");
        foreach (int index in new[] { 3, 6 })
        {
            Kepler3DCase k = Kepler3DMatrix[index];
            double[] pt = ParseKeplerGolden(k.PT);
            double[] vt = ParseKeplerGolden(k.VT);
            var body = new OrbitingBody
            {
                Name = "Central",
                StandardGravitationalParameter = k.Mu,
                Radius = 1d
            };
            var sys = new StarSystem(body);
            var c = new StudyShipMatrixCase
            {
                Label = "WH " + k.Label,
                Sys = sys,
                Central = 0,
                P0 = new Vector3d(k.P0[0], k.P0[1], k.P0[2]),
                V0 = new Vector3d(k.V0[0], k.V0[1], k.V0[2]),
                Mass0 = 1000d,
                Seconds = k.TotalSeconds,
                Tau = 2d * Math.PI * Math.Sqrt(1d / k.Mu),
                StepScale = 1d
            };
            double h = c.Tau / 16d;
            var r = StudyShipMatrixFixed(c, "WH-Y6", h);
            // Kepler3DMatrix is dimensionless (a=1, mu=2), so the raw difference
            // is in normalized units, not metres. The old "m" label read a
            // 1e-6 gate as 1e-6 m while it was ~150 km. Report both, and scale
            // with 1 length unit = 1 AU, 1 velocity unit = sqrt(2*mu_sun/AU).
            double dpNorm = (r.P - new Vector3d(pt[0], pt[1], pt[2])).Magnitude;
            double dvNorm = (r.V - new Vector3d(vt[0], vt[1], vt[2])).Magnitude;
            double dpMeters = dpNorm * StudyAU;
            double dvMps = dvNorm * StudyKeplerVelocityScale;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "WH-Y6 {0} h={1:F6} dp={2:E3} norm ({3:E3} m at 1 AU) dv={4:E3} norm ({5:E3} m/s)",
                k.Label, h, dpNorm, dpMeters, dvNorm, dvMps));
            Check(dpNorm < 1e-8d, "WH eccentric " + k.Label + " dp",
                dpNorm.ToString("E3", CultureInfo.InvariantCulture) + " norm ("
                + dpMeters.ToString("E3", CultureInfo.InvariantCulture) + " m at 1 AU)");
            Check(dvNorm < 3e-7d, "WH eccentric " + k.Label + " dv",
                dvNorm.ToString("E3", CultureInfo.InvariantCulture) + " norm ("
                + dvMps.ToString("E3", CultureInfo.InvariantCulture) + " m/s)");
        }
    }

    private static void StudyShipMatrixSetTargets(StudyShipMatrixCase c)
    {
        c.AccuracyTarget = 1d;
        c.AlternativeTarget = 10d;
        if (c.Label.IndexOf("GTO", StringComparison.Ordinal) >= 0
            || c.Label.IndexOf("GEO", StringComparison.Ordinal) >= 0)
        {
            c.AccuracyTarget = 2d;
            c.AlternativeTarget = 5d;
        }
        else if (c.Label.IndexOf("Molniya", StringComparison.Ordinal) >= 0)
        {
            c.AccuracyTarget = 2d;
            c.AlternativeTarget = 10d;
        }
        else if (c.Label.StartsWith("Sun", StringComparison.OrdinalIgnoreCase))
        {
            c.AccuracyTarget = 1d;
            c.AlternativeTarget = 100d;
        }

        if (c.Label.IndexOf("1000 orbits", StringComparison.Ordinal) >= 0)
        {
            // Requirement: <= 1 m over 64 days (~0.016 m/day). The local-frame
            // production path measures ~0.01 m; the old absolute path was ~60 m.
            c.AccuracyTarget = 1d;
            c.AlternativeTarget = 1d;
        }
        else if (c.Label.IndexOf("HEO", StringComparison.OrdinalIgnoreCase) >= 0
            || c.Label.IndexOf("hyperbolic", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            c.AccuracyTarget = 1d;
            c.AlternativeTarget = 1d;
        }
        else if (c.Label.IndexOf("world chunk", StringComparison.Ordinal) >= 0)
        {
            // 1-day chunks over 30 days: the local-frame carry-over keeps the
            // restart cost below the 1 m mission tolerance.
            c.AccuracyTarget = 1d;
            c.AlternativeTarget = 1d;
        }

        if (c.Perturbed)
        {
            c.AccuracyTarget = Math.Max(c.AccuracyTarget, 2d);
        }

        if (c.Thrust)
        {
            c.AccuracyTarget = Math.Max(c.AccuracyTarget, 2d);
            c.AlternativeTarget = Math.Max(c.AlternativeTarget, 10d);
        }
    }

    private static void StudyShipMatrixGate(
        StudyShipMatrixCase c, StudyShipMatrixRefs refs, List<StudyShipMatrixRow> rows)
    {
        StudyShipMatrixSetTargets(c);
        double productionTarget = c.AccuracyTarget;

        // Информационная чувствительность к формулировке силы при данной
        // запечённой эфемериде, не мера ошибки эталона и не ошибка интегратора.
        // На 1000 витках 8.5 м (рост нестепенной, локальные показатели 2.9-3.9);
        // достаточно сил ~2e-13 м/с^2 для такого сдвига за 64 суток.
        // Production и certified согласуются на 1.2 см, dd решает ОДУ с другим
        // непрямым членом.
        if (refs.HasFormulation && double.IsFinite(refs.Formulation))
        {
            double ddError = double.IsFinite(refs.DdError) ? refs.DdError : refs.DdSelf;
            double localError = refs.CertifiedFloor;
            string localText = double.IsFinite(localError)
                ? localError.ToString("E3", CultureInfo.InvariantCulture) + " m"
                : "uncertified";
            if (c.UseTaylorDdReference)
            {
                Console.WriteLine("INFO formulation Taylor world-frame golden differs from certified local by "
                    + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture)
                    + " m: " + StudyShipFormulationNote);
            }
            else if (double.IsFinite(localError) && double.IsFinite(ddError))
            {
                double budget = 10d * Math.Max(localError, ddError);
                Check(refs.Formulation <= budget,
                    "matrix formulation local vs local-DD " + c.Label,
                    refs.Formulation.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                    + budget.ToString("E3", CultureInfo.InvariantCulture) + " m (10x max("
                    + localError.ToString("E3", CultureInfo.InvariantCulture) + ", "
                    + ddError.ToString("E3", CultureInfo.InvariantCulture) + "))");
                Console.WriteLine("INFO formulation local vs local-DD " + c.Label + ": "
                    + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture) + " m (local "
                    + localText + ", DD " + ddError.ToString("E3", CultureInfo.InvariantCulture)
                    + " m)");
            }
            else
            {
                Console.WriteLine("INFO formulation local vs local-DD " + c.Label + ": "
                    + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture)
                    + " m (local " + localText + ", DD "
                    + (double.IsFinite(ddError)
                        ? ddError.ToString("E3", CultureInfo.InvariantCulture) + " m"
                        : "uncertified") + "; not gated)");
            }

            if (StudyShipMatrixBaselines.TryGetValue(c.Label, out StudyShipMatrixBaseline baseline)
                && double.IsFinite(baseline.Formulation))
            {
                Console.WriteLine("INFO formulation regression " + c.Label + ": "
                    + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture) + " m vs recorded "
                    + baseline.Formulation.ToString("E3", CultureInfo.InvariantCulture) + " m (not gated)");
            }
        }
        double alternativeTarget = c.AlternativeTarget;

        StudyShipMatrixRow production = null;
        StudyShipMatrixRow unalignedProduction = null;
        StudyShipMatrixRow burnSplit = null;
        StudyShipMatrixRow localAdaptiveDop853 = null;
        StudyShipMatrixRow wh = null;
        StudyShipMatrixRow fixed600 = null;
        foreach (StudyShipMatrixRow r in rows)
        {
            if (r.Method == "DOP853" && r.Frame == "world"
                && r.Note == "production" && r.Finite)
            {
                production = r;
                unalignedProduction = r;
            }

            if (c.Thrust && r.Method == "DOP853" && r.Frame == "world"
                && r.Note == "burn-split" && r.Finite)
            {
                production = r;
                burnSplit = r;
            }

            if (r.Method == "DOP853" && r.Frame == "local" && r.Steps < 0L
                && r.Finite && double.IsFinite(r.Dp)
                && (localAdaptiveDop853 == null || r.Dp < localAdaptiveDop853.Dp))
            {
                localAdaptiveDop853 = r;
            }

            if (r.Method == "WH-Y6" && r.Frame == "local" && r.Finite
                && double.IsFinite(r.Dp)
                && (wh == null || r.Dp < wh.Dp))
            {
                wh = r;
            }

            if (r.Method == "DOP853" && r.Frame == "world"
                && r.Note == "trial-mass check")
            {
                fixed600 = r;
            }
        }

        bool hardEdgeBurn = c.Label.IndexOf("unaligned", StringComparison.OrdinalIgnoreCase) >= 0;
        bool consistencyOnly = c.Label.IndexOf("Earth-Moon flyby", StringComparison.OrdinalIgnoreCase) >= 0;
        bool certified = refs.LocalAsymptotic;
        double refResolution = double.IsNaN(refs.LocalError) ? refs.LocalFinestDelta : refs.LocalError;
        if (hardEdgeBurn)
        {
            // A hard on/off edge is C0, not C1. The unsplit production path is not
            // evidence; the edge-split path is the real path, and it can be gated
            // against the reference if that reference did reach an asymptotic pair.
            if (refs.LocalAsymptotic)
            {
                Console.WriteLine("INFO hard-edge reference asymptotic: " + refs.LocalKind);
                Check(refResolution < 0.1d * productionTarget,
                    "matrix hard-edge reference resolution " + c.Label,
                    refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m < "
                    + (0.1d * productionTarget).ToString("G3", CultureInfo.InvariantCulture) + " m");
            }
            else
            {
                Console.WriteLine("INFO hard-edge burn reference (not certified): " + refs.LocalKind);
            }
        }
        else if (consistencyOnly)
        {
            // The lunar flyby reference does not converge (its finest pair is
            // disagreement, not resolution), so it cannot certify accuracy. The
            // case is still gated: production and an independent local adaptive
            // DOP853 run must land on the same trajectory.
            Console.WriteLine("INFO Earth-Moon flyby reference (no converging reference): " + refs.LocalKind);
        }
        else
        {
            Check(certified || refs.LocalResolved,
                "matrix reference certification " + c.Label,
                refs.LocalKind);
            Check((certified || refs.LocalResolved) && refResolution < 0.1d * productionTarget,
                "matrix reference resolution " + c.Label,
                refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m < "
                + (0.1d * productionTarget).ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

        if (consistencyOnly)
        {
            double separation = StudyShipRowSeparation(production, localAdaptiveDop853);
            Check(production != null && localAdaptiveDop853 != null,
                "matrix flyby production and local adaptive present " + c.Label,
                production == null ? "missing production"
                    : localAdaptiveDop853 == null ? "missing local adaptive" : "both present");
            Check(double.IsFinite(separation) && separation <= productionTarget,
                "matrix flyby production vs local adaptive DOP853 " + c.Label,
                double.IsFinite(separation)
                    ? separation.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m"
                    : "missing");
            Console.WriteLine("flyby separation production/local-adaptive: "
                + (double.IsFinite(separation)
                    ? separation.ToString("E3", CultureInfo.InvariantCulture) + " m"
                    : "missing") + ", reference finest delta="
                + refs.LocalFinestDelta.ToString("E2", CultureInfo.InvariantCulture) + " m");
        }

        if (hardEdgeBurn)
        {
            Console.WriteLine("INFO hard-edge burn: unaligned production dp="
                + (unalignedProduction == null ? "missing"
                    : unalignedProduction.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m")
                + ", burn-split dp="
                + (burnSplit == null ? "missing"
                    : burnSplit.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m")
                + " — no accuracy claim for the unsplit path; split path gated below");
            Check(burnSplit != null && burnSplit.Finite
                    && burnSplit.Dp + refResolution <= productionTarget,
                "matrix hard-edge burn-split + reference " + c.Label,
                burnSplit == null ? "missing"
                    : burnSplit.Dp.ToString("E3", CultureInfo.InvariantCulture) + " + "
                      + refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }
        else if (!consistencyOnly)
        {
            Check(production != null && production.Dp <= productionTarget,
                "matrix production DOP853 " + c.Label,
                production == null ? "missing"
                    : production.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
            Check(localAdaptiveDop853 != null && localAdaptiveDop853.Dp <= alternativeTarget,
                "matrix local adaptive DOP853 " + c.Label,
                localAdaptiveDop853 == null ? "missing"
                    : localAdaptiveDop853.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + alternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
            Check(production != null && double.IsFinite(production.Dv) && production.Dv <= 1e-3d,
                "matrix production dv " + c.Label,
                production == null ? "missing"
                    : production.Dv.ToString("E3", CultureInfo.InvariantCulture) + " m/s <= 1E-03");
            Check(production != null && double.IsFinite(production.Da) && production.Da <= 0.1d,
                "matrix production da " + c.Label,
                production == null ? "missing"
                    : production.Da.ToString("E3", CultureInfo.InvariantCulture) + " m <= 0.1 m");
            Check(production != null && double.IsFinite(production.De) && production.De <= 1e-6d,
                "matrix production de " + c.Label,
                production == null ? "missing"
                    : production.De.ToString("E3", CultureInfo.InvariantCulture) + " <= 1E-06");
        }

        if (!c.Perturbed && !c.Thrust && production != null)
        {
            Check(production.DEnergy < 1e-8d,
                "matrix production dE/E " + c.Label,
                production.DEnergy.ToString("E2", CultureInfo.InvariantCulture) + " < 1E-08");
        }

        // Two independent discretizations of the same force, in different frames
        // and with different step control, must agree: this is what makes the
        // uncertified-reference cases meaningful and catches a common-mode frame bug.
        if (!c.Thrust && !consistencyOnly && production != null && localAdaptiveDop853 != null)
        {
            double separation = StudyShipRowSeparation(production, localAdaptiveDop853);
            Check(double.IsFinite(separation) && separation <= alternativeTarget,
                "matrix production vs local adaptive DOP853 " + c.Label,
                separation.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                + alternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

        bool hyperbolic = c.Label.IndexOf("hyperbolic", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hyperbolic && !c.Thrust && !consistencyOnly)
        {
            Check(wh != null && wh.Dp <= alternativeTarget,
                "matrix WH-Y6 " + c.Label,
                wh == null ? "missing"
                    : wh.Config + " dp=" + wh.Dp.ToString("E3", CultureInfo.InvariantCulture)
                      + " m <= " + alternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }
        else if (consistencyOnly && wh != null)
        {
            // The flyby reference is uncertified; WH is reported, not graded
            // against it. The production-vs-independent-adaptive gate above is
            // the only accuracy statement for this case.
            Console.WriteLine("INFO WH-Y6 " + c.Label + ": " + wh.Config + " dp="
                + wh.Dp.ToString("E3", CultureInfo.InvariantCulture)
                + " m vs uncertified reference (not gated)");
        }

        // A NaN in any fixed-step row must fail the case. The old suite only
        // printed the count, so RK4/PEFRL/Y6/DOPRI5 could silently break as long
        // as production and WH stayed finite. WH on an unbound orbit is the one
        // expected rejection (asserted explicitly below).
        foreach (StudyShipMatrixRow r in rows)
        {
            bool expectedReject = hyperbolic && r.Method == "WH-Y6";
            Check(r.Finite || expectedReject,
                "matrix row finite " + c.Label + " " + r.Method + "/" + r.Config,
                r.Finite ? "finite" : "non-finite" + (expectedReject ? " (expected WH reject)" : ""));
        }

        if (c.Thrust && !hardEdgeBurn)
        {
            Check(fixed600 != null && fixed600.Finite && fixed600.Dp <= productionTarget,
                "matrix thrust 600 s production chunk " + c.Label,
                fixed600 == null ? "missing"
                    : fixed600.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

        if (hyperbolic)
        {
            bool rejected = false;
            foreach (StudyShipMatrixRow r in rows)
            {
                if (r.Method == "WH-Y6" && !r.Finite)
                {
                    rejected = true;
                    break;
                }
            }

            Check(rejected, "matrix WH rejects unbound orbit " + c.Label,
                rejected ? "non-finite row" : "silently finite");
        }
    }

    private sealed class StudyShipComparisonRule
    {
        public string Label;
        public double Target;
        /// <summary>Cap on RHS-equivalent cost, not raw Taylor jet calls.</summary>
        public long CheapestCostCap;
        public double WhToProductionRatio;
    }

    private static void StudyShipMatrixComparisons(
        List<StudyShipMatrixRow> all, List<string> labels, Dictionary<string, double> floors)
    {
        // The suite used to only check "no regression". These statements are the
        // actual study claim: which family is cheapest at a useful accuracy, and
        // by how much it beats the production path. Caps are ~3x the measured
        // value, so a regression fails while noise does not.
        Console.WriteLine();
        Console.WriteLine("=== integrator comparison claims (failing gates) ===");
        var rules = new[]
        {
            new StudyShipComparisonRule
            {
                Label = "Earth LEO 400 km circular", Target = 1d,
                CheapestCostCap = 5000L, WhToProductionRatio = 0.25d
            },
            new StudyShipComparisonRule
            {
                Label = "Earth LEO 400 km polar", Target = 1d,
                CheapestCostCap = 5000L, WhToProductionRatio = 0.35d
            },
            new StudyShipComparisonRule
            {
                // Rebased 2026-11-02: WH-Y6's double RHS per sub-step was removed
                // (FSAL reuse), so the cheapest <=1 m row is h=74.59 s at 8,208
                // RHS-equivalents; 3x that is the cap.
                Label = "Earth LEO 400 km sun-synchronous", Target = 1d,
                CheapestCostCap = 25000L, WhToProductionRatio = 0.35d
            },
            new StudyShipComparisonRule
            {
                // 3x the measured 25,200 calls; the ratio is the actual claim
                // (0.449 measured), not a 3x-of-measured bound.
                Label = "Earth GEO 35786 km", Target = 2d,
                CheapestCostCap = 75000L, WhToProductionRatio = 0.6d
            },
            new StudyShipComparisonRule
            {
                Label = "Sun 1 AU circular", Target = 1d,
                CheapestCostCap = 5000L, WhToProductionRatio = 0.1d
            },
            new StudyShipComparisonRule
            {
                Label = "Earth LEO 400 km, 1000 orbits (~64 d)", Target = 1d,
                CheapestCostCap = 350000L, WhToProductionRatio = 0.5d
            },
            new StudyShipComparisonRule
            {
                Label = "Earth LEO 400 km, 30 d, world chunk 1 d", Target = 1d,
                CheapestCostCap = 160000L, WhToProductionRatio = 0.5d
            }
        };

        foreach (StudyShipComparisonRule rule in rules)
        {
            floors.TryGetValue(rule.Label, out double floor);
            double target = Math.Max(rule.Target, double.IsFinite(floor) ? floor : rule.Target);
            if (double.IsFinite(floor) && floor > rule.Target)
            {
                Console.WriteLine("note " + rule.Label + ": target "
                    + rule.Target.ToString("G", CultureInfo.InvariantCulture)
                    + " m is below the reference floor "
                    + floor.ToString("E3", CultureInfo.InvariantCulture)
                    + " m; ranking at the floor, the sub-floor claim is not made");
            }

            StudyShipMatrixRow cheapest = null;
            StudyShipMatrixRow cheapestMs = null;
            StudyShipMatrixRow cheapestWh = null;
            StudyShipMatrixRow production = null;
            for (int i = 0; i < all.Count; i++)
            {
                if (labels[i] != rule.Label)
                {
                    continue;
                }

                StudyShipMatrixRow r = all[i];
                if (r.Method == "DOP853" && r.Frame == "world"
                    && r.Note == "production" && r.Finite)
                {
                    production = r;
                }

                double ranked = StudyShipMatrixRanked(r, floor);
                if (r.Method == "Taylor" || r.Frame != "local" || !r.Finite
                    || !double.IsFinite(ranked) || r.Cost <= 0L || ranked > target)
                {
                    // Taylor's x12 cost is not calibrated; it is excluded from the
                    // production cost claim (its measured ms remains in the table).
                    continue;
                }

                if (cheapest == null || r.Cost < cheapest.Cost
                    || (r.Cost == cheapest.Cost
                        && ranked < StudyShipMatrixRanked(cheapest, floor)))
                {
                    cheapest = r;
                }

                if (cheapestMs == null || r.Ms < cheapestMs.Ms
                    || (r.Ms == cheapestMs.Ms
                        && ranked < StudyShipMatrixRanked(cheapestMs, floor)))
                {
                    cheapestMs = r;
                }

                if (r.Method == "WH-Y6"
                    && (cheapestWh == null || r.Cost < cheapestWh.Cost))
                {
                    cheapestWh = r;
                }
            }

            Check(cheapest != null,
                "comparison cheapest <= " + target.ToString("G", CultureInfo.InvariantCulture)
                + " m " + rule.Label,
                cheapest == null ? "no local row meets the target" : cheapest.Method + "/" + cheapest.Config);
            if (cheapest != null)
            {
                Check(cheapest.Cost <= rule.CheapestCostCap,
                    "comparison cheapest RHS-eq cost " + rule.Label,
                    cheapest.Method + "/" + cheapest.Config + " cost="
                    + cheapest.Cost.ToString("N0", CultureInfo.InvariantCulture) + " ("
                    + cheapest.Calls.ToString("N0", CultureInfo.InvariantCulture) + " calls) <= "
                    + rule.CheapestCostCap.ToString("N0", CultureInfo.InvariantCulture));
                // RHS-equivalent cost is a proxy. If the cost winner is also far
                // slower than the measured wall-clock winner, the "cheaper" claim
                // does not hold in the resource the user actually spends.
                Check(cheapestMs != null && cheapest.Ms <= 3d * cheapestMs.Ms,
                    "comparison cheapest cost vs measured ms " + rule.Label,
                    cheapest.Ms.ToString("F1", CultureInfo.InvariantCulture) + " ms <= 3x "
                    + (cheapestMs == null ? "missing" : cheapestMs.Ms.ToString("F1", CultureInfo.InvariantCulture))
                    + " ms (" + (cheapestMs == null ? "none" : cheapestMs.Method + "/" + cheapestMs.Config) + ")");
            }

            if (rule.WhToProductionRatio > 0d)
            {
                Check(production != null && cheapestWh != null,
                    "comparison WH and production present " + rule.Label,
                    production == null ? "missing production"
                        : cheapestWh == null ? "no WH row meets the target" : "both present");
                if (production != null && cheapestWh != null)
                {
                    long cap = (long)(rule.WhToProductionRatio * production.Calls);
                    Check(cheapestWh.Calls <= cap,
                        "comparison WH vs production calls " + rule.Label,
                        "WH " + cheapestWh.Config + " calls="
                        + cheapestWh.Calls.ToString("N0", CultureInfo.InvariantCulture) + " <= "
                        + rule.WhToProductionRatio.ToString("G2", CultureInfo.InvariantCulture)
                        + " x production " + production.Calls.ToString("N0", CultureInfo.InvariantCulture)
                        + " = " + cap.ToString("N0", CultureInfo.InvariantCulture));
                    // The cost claim must survive the measured wall clock too: a
                    // row that wins only on an assumed per-call price is not a
                    // resource win. ms is the sequential min-of-up-to-3 value.
                    Check(cheapestWh.Ms <= production.Ms,
                        "comparison WH vs production ms " + rule.Label,
                        "WH " + cheapestWh.Ms.ToString("F1", CultureInfo.InvariantCulture)
                        + " ms <= production " + production.Ms.ToString("F1", CultureInfo.InvariantCulture)
                        + " ms");
                    Console.WriteLine(
                        "claim " + rule.Label + ": cheapest " + cheapest.Method + "/" + cheapest.Config
                        + " " + cheapest.Cost.ToString("N0", CultureInfo.InvariantCulture)
                        + " RHS-eq (" + cheapest.Calls.ToString("N0", CultureInfo.InvariantCulture)
                        + " calls), WH " + cheapestWh.Calls.ToString("N0", CultureInfo.InvariantCulture)
                        + " vs production " + production.Calls.ToString("N0", CultureInfo.InvariantCulture)
                        + " calls (" + (cheapestWh.Calls / (double)Math.Max(production.Calls, 1L))
                            .ToString("F3", CultureInfo.InvariantCulture) + "x)");
                }
            }
        }
    }

    private static void StudyShipMatrixRegression(
        List<StudyShipMatrixRow> all, List<string> labels,
        Dictionary<string, double> formulations, Dictionary<string, double> floors)
    {
        Console.WriteLine();
        Console.WriteLine("=== regression report (3x rebased baseline on the max-over-trajectory metric; failing gate) ===");
        int regressions = 0;
        int comparisons = 0;
        foreach (var entry in StudyShipMatrixBaselines)
        {
            string label = entry.Key;
            StudyShipMatrixBaseline baseline = entry.Value;
            floors.TryGetValue(label, out double floor);
            StudyShipMatrixRow production = null;
            StudyShipMatrixRow bestLocal = null;
            for (int i = 0; i < all.Count; i++)
            {
                if (labels[i] != label)
                {
                    continue;
                }

                StudyShipMatrixRow r = all[i];
                if (r.Method == "DOP853" && r.Frame == "world"
                    && r.Note == "production" && r.Finite)
                {
                    production = r;
                }

                if (r.Frame == "local" && r.Finite && double.IsFinite(r.Dp))
                {
                    double ranked = StudyShipMatrixRanked(r, floor);
                    if (bestLocal == null)
                    {
                        bestLocal = r;
                    }
                    else
                    {
                        double bestRanked = StudyShipMatrixRanked(bestLocal, floor);
                        if (ranked < bestRanked
                            || (ranked == bestRanked && r.Cost < bestLocal.Cost))
                        {
                            bestLocal = r;
                        }
                    }
                }
            }

            double referenceFloor = double.IsFinite(floor) ? floor : 0d;

            comparisons++;
            double productionDp = production == null ? double.NaN : production.Dp;
            // A gate below the reference's own convergence floor is measuring
            // reference noise. Raise the limit to the floor instead of failing on a
            // number the suite cannot resolve.
            double productionLimit = Math.Max(3d * baseline.Production, referenceFloor);
            bool productionOk = double.IsFinite(productionDp)
                && productionDp <= productionLimit;
            if (!productionOk)
            {
                regressions++;
            }

            string productionDetail = production == null
                ? "missing"
                : productionDp.ToString("E3", CultureInfo.InvariantCulture) + " m vs "
                  + productionLimit.ToString("E3", CultureInfo.InvariantCulture) + " m"
                  + (productionLimit > 3d * baseline.Production ? " (reference floor)" : " (3x baseline)");
            Check(productionOk, "matrix regression production " + label, productionDetail);
            Console.WriteLine("INFO production " + label + ": " + productionDetail);

            comparisons++;
            double bestLocalDp = bestLocal == null ? double.NaN : bestLocal.Dp;
            double bestLocalRanked = bestLocal == null
                ? double.NaN
                : StudyShipMatrixRanked(bestLocal, floor);
            double bestLocalLimit = Math.Max(3d * baseline.BestLocal, referenceFloor);
            bool bestLocalOk = double.IsFinite(bestLocalRanked)
                && bestLocalRanked <= bestLocalLimit;
            if (!bestLocalOk)
            {
                regressions++;
            }

            string bestLocalDetail = bestLocal == null
                ? "missing"
                : bestLocal.Method + "/" + bestLocal.Config + " "
                  + bestLocalDp.ToString("E3", CultureInfo.InvariantCulture) + " m"
                  + (bestLocalRanked > bestLocalDp ? " (at reference floor)" : "")
                  + " vs " + bestLocalLimit.ToString("E3", CultureInfo.InvariantCulture) + " m"
                  + (bestLocalLimit > 3d * baseline.BestLocal ? " (reference floor)" : " (3x baseline)");
            Check(bestLocalOk, "matrix regression best local " + label, bestLocalDetail);
            Console.WriteLine("INFO best local " + label + ": " + bestLocalDetail);

            if (!double.IsNaN(baseline.Formulation)
                && formulations.TryGetValue(label, out double formulation)
                && !double.IsNaN(formulation))
            {
                // Informational only: the DD cross-check is not a certified
                // reference, so its difference cannot fail the run. Keep the
                // recorded value in the log so a real regression is still visible.
                Console.WriteLine("INFO formulation " + label + ": "
                    + formulation.ToString("E3", CultureInfo.InvariantCulture)
                    + " m (recorded " + baseline.Formulation.ToString("E3", CultureInfo.InvariantCulture)
                    + " m, not gated)");
            }
        }

        Check(regressions == 0, "matrix regression report",
            regressions + " of " + comparisons + " comparisons outside 3x");
        Console.WriteLine("regression report: " + regressions
            + " of " + comparisons + " comparisons outside 3x");
    }

    private static int Test549_SpacecraftOrbitIntegrators()
    {
        Console.WriteLine("--- Spacecraft orbit integrator matrix: all methods, all conditions ---");
        int failuresBefore = failures;

        // The production field is the baked N-body ephemeris: its body
        // accelerations are dynamically consistent with the full force, which is
        // what lets the local frame subtract the central body's acceleration
        // exactly. An analytic Kepler hierarchy is NOT consistent for that
        // subtraction (a Moon-centred step drifts against an Earth-centred one),
        // so it cannot be used as a "smooth" drop-in here.
        StarSystem gravity = StudyShipMatrixSystem(false, false);
        // Keep the perturbations isolated: a "J2" case must not silently add
        // atmosphere, and a "drag" case must not silently add J2. The old shared
        // "perturbed" system enabled both, so neither label matched its physics.
        StarSystem j2OnlySystem = StudyShipMatrixSystem(true, false);
        StarSystem dragOnlySystem = StudyShipMatrixSystem(false, true);
        double seamGravity = StudyShipMatrixSeamJump(gravity, "gravity");
        double seamPerturbed = StudyShipMatrixSeamJump(j2OnlySystem, "J2");
        double seamDrag = StudyShipMatrixSeamJump(dragOnlySystem, "drag");
        Check(seamGravity < 0.5d && seamPerturbed < 0.5d && seamDrag < 0.5d,
            "matrix ephemeris seam jump",
            "gravity " + seamGravity.ToString("E3", CultureInfo.InvariantCulture)
            + " m, J2 " + seamPerturbed.ToString("E3", CultureInfo.InvariantCulture)
            + " m, drag " + seamDrag.ToString("E3", CultureInfo.InvariantCulture)
            + " m < 0.5 m");
        var cases = new List<StudyShipMatrixCase>();

        const double day = 86400d;
        const double year = 365.25d * 86400d;

        cases.Add(StudyShipMatrixCaseOrbit("Earth LEO 400 km circular",
            gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 0d, 0d, 0d, day, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Earth LEO 400 km polar",
            gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 90d, 0d, 0d, day, false, true, 1d));
        // Sun-synchronous is a J2 effect: run it with J2, no atmosphere.
        cases.Add(StudyShipMatrixCaseOrbit("Earth LEO 400 km sun-synchronous",
            j2OnlySystem, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 98d, 0d, 0d, day, true, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Earth GTO 400 x 40000 km",
            gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e7d, 28.5d, 0d, 0d, 2d * day, false, true, 1d));
        {
            var molniya = StudyShipMatrixCaseOrbit("Earth Molniya 500 x 40000 km",
                gravity, 1, 6.371e6d + 5e5d, 6.371e6d + 4e7d, 63.4d, 0d, 270d, 7d * day, false, true, 1d);
            // The local DD cross-check is now a real check, so its own convergence
            // must be below the certified floor. At h=60 s Y6C's error on this arc
            // was 1.7 m; 10 s brings it under the local floor without dominating
            // the suite (the local force is much cheaper than the world path).
            molniya.ReferenceStepSeconds = 10d;
            cases.Add(molniya);
        }
        cases.Add(StudyShipMatrixCaseOrbit("Earth GEO 35786 km",
            gravity, 1, 4.2164e7d, 4.2164e7d, 0d, 0d, 0d, 30d * day, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Sun 1 AU circular",
            gravity, 0, StudyAU, StudyAU, 0d, 0d, 0d, year, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Sun 0.5 x 2 AU eccentric",
            gravity, 0, 0.5d * StudyAU, 2d * StudyAU, 0d, 0d, 0d, year, false, true, 1d));
        {
            var j2Only = StudyShipMatrixCaseOrbit("Earth 400 km + J2",
                j2OnlySystem, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 0d, 0d, 0d, day, true, false, 1d);
            // The J2 system has no atmosphere, so no drag is added; the label and
            // the force model now match.
            j2Only.Drag = null;
            cases.Add(j2Only);
        }

        // Drag without J2: the atmosphere is the only perturbation.
        cases.Add(StudyShipMatrixCaseOrbit("Earth 250 km + atmosphere drag",
            dragOnlySystem, 1, 6.371e6d + 2.5e5d, 6.371e6d + 2.5e5d, 0d, 0d, 0d, day, true, false, 0.5d));

        {
            StudyShipMatrixOrbit(gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d,
                0d, 0d, 0d, out Vector3d tp, out Vector3d tv);
            var thrust = new StudyShipMatrixCase
            {
                Label = "Earth LEO 400 km + 600 s prograde burn",
                Sys = gravity,
                Central = 1,
                P0 = tp,
                V0 = tv,
                Mass0 = 1000d,
                Seconds = day,
                Thrust = true,
                ThrustDirection = tv.Normalized,
                ThrustStart = StudyShipT0 + 96d,
                ThrustSeconds = 600d,
                ThrustNewtons = 2000d,
                ThrustIsp = 300d,
                ThrustRampSeconds = 20d,
                AllowTaylor = false,
                WorldChunkSeconds = 60d,
                Tau = StudyShipMatrixTau(6.371e6d + 4e5d,
                    gravity.AllBodies[1].ResolveStandardGravitationalParameter()),
                StepScale = 0.02d
            };
            cases.Add(thrust);
        }

        {
            // The baked N-body field is the one the local frame is designed for:
            // its body accelerations include the mutual pulls, so subtracting the
            // central body's Newtonian acceleration is exact.
            // d-пары ~1e-5 м лежат на уровне накопленного округления (оценка
            // eps*|r|*sqrt(N) ~ 3e-5 м), поэтому ratio неинформативен; сценарий
            // consistency-only, не гейтится.
            OrbitingBody moon = gravity.AllBodies[2];
            gravity.EvaluateBodyState(moon, StudyShipT0, out Vector3d mp, out Vector3d mv);
            gravity.EvaluateBodyState(gravity.AllBodies[1], StudyShipT0,
                out Vector3d ep, out Vector3d ev);
            var flyby = new StudyShipMatrixCase
            {
                Label = "Earth-Moon flyby inside lunar SOI",
                Sys = gravity,
                Central = 1,
                P0 = (mp - ep) + new Vector3d(0d, 2e7d, 0d),
                V0 = (mv - ev) + new Vector3d(0d, 0d, 500d),
                Mass0 = 1000d,
                Seconds = 2d * day,
                AllowTaylor = true,
                Tau = 80000d,
                StepScale = 0.25d
            };
            cases.Add(flyby);
        }

        {
            var longLeo = StudyShipMatrixCaseOrbit("Earth LEO 400 km, 1000 orbits (~64 d)",
                gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 0d, 0d, 0d, day, false, true, 1d);
            longLeo.Seconds = 1000d * longLeo.Tau;
            longLeo.ReferenceStepSeconds = 10d;
            longLeo.UseTaylorDdReference = true;
            cases.Add(longLeo);
        }

        {
            var heo = StudyShipMatrixCaseOrbit("Earth HEO 300 x 120000 km",
                gravity, 1, 6.371e6d + 3e5d, 1.2e8d, 0d, 0d, 0d, 5d * day, false, true, 1d);
            // 60 s left the DD check with 0.74 m of its own error, above the
            // certified 1.6e-4 m floor. 10 s puts it below the floor.
            heo.ReferenceStepSeconds = 10d;
            cases.Add(heo);
        }

        {
            const double earthMu = 3.986004418e14d;
            double rp = 6.371e6d + 3e5d;
            double e = 1.5d;
            double vp = Math.Sqrt(earthMu * (1d + e) / rp);
            cases.Add(new StudyShipMatrixCase
            {
                Label = "Earth hyperbolic flyby e=1.5",
                Sys = gravity,
                Central = 1,
                P0 = new Vector3d(rp, 0d, 0d),
                V0 = new Vector3d(0d, vp, 0d),
                Mass0 = 1000d,
                Seconds = day,
                AllowTaylor = false,
                Tau = StudyShipMatrixTau(rp, earthMu),
                StepScale = 1d,
                ScaleLength = 1e8d,
                ReferenceStepSeconds = 5d
            });
        }

        {
            var chunk = StudyShipMatrixCaseOrbit("Earth LEO 400 km, 30 d, world chunk 1 d",
                gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 0d, 0d, 0d, 30d * day, false, true, 1d);
            chunk.WorldChunkSeconds = 86400d;
            chunk.ReferenceStepSeconds = 20d;
            cases.Add(chunk);
        }

        {
            StudyShipMatrixOrbit(gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d,
                0d, 0d, 0d, out Vector3d up, out Vector3d uv);
            cases.Add(new StudyShipMatrixCase
            {
                Label = "Earth LEO 400 km + unaligned 600 s burn",
                Sys = gravity,
                Central = 1,
                P0 = up,
                V0 = uv,
                Mass0 = 1000d,
                Seconds = day,
                Thrust = true,
                ThrustDirection = uv.Normalized,
                ThrustStart = StudyShipT0 + 103.7d,
                ThrustSeconds = 600d,
                ThrustNewtons = 2000d,
                ThrustIsp = 300d,
                ThrustRampSeconds = 0d,
                AllowTaylor = false,
                WorldChunkSeconds = 60d,
                Tau = StudyShipMatrixTau(6.371e6d + 4e5d,
                    gravity.AllBodies[1].ResolveStandardGravitationalParameter()),
                StepScale = 0.02d,
                ScaleLength = 6.371e6d + 4e5d
            });
        }

        string resultsDir = StudyShipMatrixResultsDir();
        string csvPath = Path.Combine(resultsDir, "Test549_SpacecraftOrbitMatrix.csv");
        var allRows = new List<StudyShipMatrixRow>();
        var allLabels = new List<string>();
        var formulations = new Dictionary<string, double>();
        var floors = new Dictionary<string, double>();

        foreach (StudyShipMatrixCase c in cases)
        {
            var caseWatch = Stopwatch.StartNew();
            StudyShipMatrixSetTargets(c);
            StudyShipMatrixRefs refs;
            try
            {
                refs = StudyShipMatrixBuildReferences(c);
            }
            catch (Exception ex)
            {
                Console.WriteLine("reference failed for " + c.Label + ": " + StudyShipMatrixNote(ex));
                Console.WriteLine(ex.ToString());
                Check(false, "reference " + c.Label, StudyShipMatrixNote(ex));
                continue;
            }

            if (StudyShipRefDiagSeams && StudyShipRefDiagSeamCase(c))
            {
                StudyShipRefDiagSeamsForCase(c, refs);
            }

            var rows = new List<StudyShipMatrixRow>();
            StudyShipMatrixRunAll(c, refs, rows);
            if (StudyShipRefDiagTriangle && StudyShipRefDiagTriangleCase(c))
            {
                StudyShipRefDiagTriangleForCase(c, refs, rows);
            }
            if (StudyShipRefDiagMoon && c.Label == "Earth-Moon flyby inside lunar SOI")
            {
                StudyShipRefDiagMoonForCase(c, refs);
            }
            if (StudyShipRefDiagDd
                && c.Label.StartsWith("Earth LEO 400 km, 1000 orbits", StringComparison.Ordinal))
            {
                StudyShipRefDiagDdForCase(c, refs);
            }
            StudyShipMatrixPrint(c, refs, rows);
            int nonFinite = 0;
            foreach (StudyShipMatrixRow r in rows)
            {
                allRows.Add(r);
                allLabels.Add(c.Label);
                if (!r.Finite)
                {
                    nonFinite++;
                }
            }

            if (nonFinite > 0)
            {
                Console.WriteLine("non-finite rows: " + nonFinite);
            }

            StudyShipMatrixGate(c, refs, rows);
            formulations[c.Label] = refs.HasFormulation ? refs.Formulation : double.NaN;
            floors[c.Label] = refs.ReferenceFloor;
            caseWatch.Stop();
            Console.WriteLine("case time: " + caseWatch.ElapsedMilliseconds + " ms");
        }

        StudyShipMatrixOrderAndTolerance();
        StudyShipMatrixCsv(allRows, allLabels, floors, csvPath);
        StudyShipMatrixScoreboard(allRows, allLabels, floors);
        StudyShipMatrixComparisons(allRows, allLabels, floors);
        StudyShipMatrixRegression(allRows, allLabels, formulations, floors);
        Console.WriteLine("failed checks: " + (failures - failuresBefore));
        Console.WriteLine("--- Spacecraft orbit integrator matrix complete ---");
        return 0;
    }

}
