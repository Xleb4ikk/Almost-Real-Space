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
        "РёРЅС„РѕСЂРјР°С†РёРѕРЅРЅР°СЏ С‡СѓРІСЃС‚РІРёС‚РµР»СЊРЅРѕСЃС‚СЊ Рє С„РѕСЂРјСѓР»РёСЂРѕРІРєРµ СЃРёР»С‹ РїСЂРё РґР°РЅРЅРѕР№ Р·Р°РїРµС‡С‘РЅРЅРѕР№ СЌС„РµРјРµСЂРёРґРµ, "
        + "РЅРµ РјРµСЂР° РѕС€РёР±РєРё СЌС‚Р°Р»РѕРЅР° Рё РЅРµ РѕС€РёР±РєР° РёРЅС‚РµРіСЂР°С‚РѕСЂР°. РќР° 1000 РІРёС‚РєР°С… 8.5 Рј "
        + "(СЂРѕСЃС‚ РЅРµСЃС‚РµРїРµРЅРЅРѕР№, Р»РѕРєР°Р»СЊРЅС‹Рµ РїРѕРєР°Р·Р°С‚РµР»Рё 2.9-3.9); РґРѕСЃС‚Р°С‚РѕС‡РЅРѕ СЃРёР» ~2e-13 Рј/СЃ^2 "
        + "РґР»СЏ С‚Р°РєРѕРіРѕ СЃРґРІРёРіР° Р·Р° 64 СЃСѓС‚РѕРє. Production Рё certified СЃРѕРіР»Р°СЃСѓСЋС‚СЃСЏ РЅР° 1.2 СЃРј, "
        + "dd СЂРµС€Р°РµС‚ РћР”РЈ СЃ РґСЂСѓРіРёРј РЅРµРїСЂСЏРјС‹Рј С‡Р»РµРЅРѕРј.";

    private static bool StudyShipMatrixInterpScore =>
        Environment.GetEnvironmentVariable("P1B_INTERP_SCORE") == "1";

    private const string StudyShipFrameLocal = "local";
    private const string StudyShipFrameProductionLocal = "production-local";

    // One list for the fixed-step matrix. Adding a method still needs its
    // StudyShipMatrixCall case, but it no longer needs a third copy of the loop.
    private static readonly string[] StudyShipMatrixFixedMethods =
    {
        "RK4", "DOPRI5", "PEFRL", "Y6", "Y6C", "WH-Y6"
    };

    // The unsplit production path is not an accuracy claim: a hard on/off thrust
    // edge is C0. The harness's sampling checkpoints force every measured row to
    // stop at the edge, so the table does not represent a truly unsplit
    // production run; the split path is gated in its place. The old 1e-6 edge
    // sub-step made the split row 33x worse than the same run without it, so it
    // has been removed (the checkpoints already provide the edge stop).
    private static readonly HashSet<string> StudyShipUnsplitPathNoClaim =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Earth LEO 400 km + unaligned 600 s burn"
        };

    // Scenarios that are deliberately outside the advantage claims. The reason is
    // printed, and the claim-coverage test still requires an entry here (or a
    // baseline/comparison entry) for every case.
    private static readonly Dictionary<string, string> StudyShipNoClaimReasons =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Earth-Moon flyby inside lunar SOI"] =
                "uncertified reference: consistency and regression lock only",
            ["Earth hyperbolic flyby e=1.5"] =
                "hyperbolic WH-Y6 is rejected; no WH advantage claim",
            ["Earth LEO 400 km + unaligned 600 s burn"] =
                "hard-edge burn: harness checkpoints force an edge stop; a truly unsplit production path is not measured"
        };

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
        /// <summary>Pin the certified DOP853 reference's start step; 0 = derive from Tau.</summary>
        public double CertifiedReferenceStepSeconds;
        public double ScaleLength = 1d;
        public bool UseTaylorDdReference;
        /// <summary>Primary mode key used by the mode map, e.g. pure-circular, j2, drag, thrust, flyby.</summary>
        public string Mode = "";
        /// <summary>Full descriptive tag set for this scenario.</summary>
        public string[] ModeTags = new string[0];
        public double AltitudeKm = double.NaN;
        public double SpeedMps = double.NaN;
        public double Eccentricity = double.NaN;
        public bool HasBreakpoints;
        /// <summary>
        /// Grade against the certified local reference alone and keep the independent
        /// DD endpoint as a diagnostic. Use this when the two force formulations are
        /// known to disagree (long LEO) and the game path is the local one; the
        /// formulation difference is printed, but it is not silently folded into the
        /// ranking floor.
        /// </summary>
        public bool LocalReferenceOnly;
        /// <summary>
        /// If the reference has a finite, target-relevant finest disagreement but no
        /// Richardson pair (stiff entry, round-off plateau), accept the finest delta
        /// as a resolved floor and say so in the reference note. This is not a
        /// convergence certificate; it is an honest bound for targets far above it.
        /// </summary>
        public bool AllowUnratioedResolution;
        /// <summary>Split the certified reference at baked-ephemeris seams.</summary>
        public bool SeamAwareReference;
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
            // Rebased 2026-11-03 to the current 5-level reference and the
            // relative-tolerance adaptive rows. The old values were 2-100x looser
            // than the measured rows, so the "3x" gates could not catch a real
            // regression. Formulations are informational (not gated).
            { "Earth LEO 400 km circular", new StudyShipMatrixBaseline(5.557e-6, 4.537e-6, 1.574e-6) },
            { "Earth LEO 400 km polar", new StudyShipMatrixBaseline(1.039e-5, 2.853e-6, 2.307e-6) },
            { "Earth LEO 400 km sun-synchronous", new StudyShipMatrixBaseline(3.105e-6, 1.448e-6, double.NaN) },
            { "Earth GTO 400 x 40000 km", new StudyShipMatrixBaseline(1.960e-5, 1.005e-6, 1.554e-6) },
            { "Earth Molniya 500 x 40000 km", new StudyShipMatrixBaseline(1.292e-2, 3.403e-5, 8.551e-5) },
            { "Earth GEO 35786 km", new StudyShipMatrixBaseline(3.210e-4, 1.093e-4, 2.192e-4) },
            { "Sun 1 AU circular", new StudyShipMatrixBaseline(6.164e-2, 3.586e-4, 1.544e-4) },
            { "Sun 0.5 x 2 AU eccentric", new StudyShipMatrixBaseline(2.511e-2, 3.523e-4, 3.112e-4) },
            { "Earth 400 km + J2", new StudyShipMatrixBaseline(4.751e-6, 3.915e-7, double.NaN) },
            { "Earth 250 km + atmosphere drag", new StudyShipMatrixBaseline(3.009e-5, 1.605e-5, double.NaN) },
            // Rebased 2026-11-04 from the current run. These two rows had the old
            // pre-split production values (9.3e-2 / 1.9e-1), which made the 3x
            // regression gate 400-1250x looser than the measured production.
            { "Earth LEO 400 km + 600 s prograde burn", new StudyShipMatrixBaseline(7.426950941686178e-5, 1.0173268040308452e-5, double.NaN) },
            { "Earth-Moon flyby inside lunar SOI", new StudyShipMatrixBaseline(2.486e-3, 5.079e-6, 1.408e-5) },
            { "Earth LEO 400 km, 1000 orbits (~64 d)", new StudyShipMatrixBaseline(1.202e-2, 6.388e-3, 8.522) },
            { "Earth HEO 300 x 120000 km", new StudyShipMatrixBaseline(1.022e-4, 4.624e-5, 1.845e-5) },
            { "Earth hyperbolic flyby e=1.5", new StudyShipMatrixBaseline(1.738e-6, 4.470e-7, 5.960e-8) },
            { "Earth LEO 400 km, 30 d", new StudyShipMatrixBaseline(3.988e-3, 1.476e-3, 5.187e-3) },
            { "Earth LEO 400 km + unaligned 600 s burn", new StudyShipMatrixBaseline(4.6662962712195526e-4, 2.994167236814948e-4, double.NaN) }
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
        /// <summary>Final energy difference against the reference (not a drift).</summary>
        public double DEnergy;
        /// <summary>Energy drift from the initial state; diagnostic only.</summary>
        public double DEnergyDrift;
        /// <summary>Raw RHS evaluations, before the production-local x3 weighting.</summary>
        public long RawCalls;
        /// <summary>
        /// RHS-equivalent cost: Calls for one-force-evaluation methods, Calls*order
        /// for Taylor (one "call" is one jet-derivative order, not one RHS).
        /// </summary>
        public long Calls;
        public long Cost;
        public long Steps;
        public double Ms;
        public int Reps = 1;
        /// <summary>Number of checkpoints actually recorded, not the array length.</summary>
        public int SampleCount;
        public Vector3d P;
        public Vector3d V;
        /// <summary>Final mass; thrust rows must not gain mass.</summary>
        public double Mass;
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
        public long RawCalls;
        public long Steps;
        public double Ms;
        public int Reps = 1;
        public int SamplesRecorded;
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

    /// <summary>
    /// The effective ramp is at most half the burn. A longer configured ramp
    /// would make the two ramp branches overlap and produce a throttle above 1;
    /// clamping turns that case into the intended triangular profile and keeps
    /// the throttle and the reference breakpoints on the same time grid.
    /// </summary>
    private static double StudyShipMatrixRampSeconds(StudyShipMatrixCase c)
    {
        return Math.Max(0d, Math.Min(c.ThrustRampSeconds, 0.5d * c.ThrustSeconds));
    }

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

        double ramp = StudyShipMatrixRampSeconds(c);
        if (ramp <= 0d)
        {
            return 1d;
        }

        if (t < start + ramp)
        {
            return Math.Min(1d, (t - start) / ramp);
        }

        if (t > end - ramp)
        {
            return Math.Max(0d, Math.Min(1d, (end - t) / ramp));
        }

        return 1d;
    }

    /// <summary>
    /// Mass-flow derivative dm/dt from the configured thrust profile. It is
    /// NEGATIVE while the engine is on, matching StudyShipMatrixAcc.Mass = -flow.
    /// Drag has zero mass flow in this model. Fixed-step splittings used to read
    /// ds[6] at one substage start, which is only first order for a ramped burn;
    /// the midpoint of the known time profile is second order and costs no extra
    /// RHS calls. Returning the signed derivative (instead of a positive
    /// magnitude) keeps every caller's `mass += dt * flow` physically correct.
    /// </summary>
    private static double StudyShipMatrixMassFlow(StudyShipMatrixCase c, double t)
    {
        if (!c.Thrust)
        {
            return 0d;
        }

        double throttle = StudyShipMatrixThrottle(c, t);
        return throttle <= 0d
            ? 0d
            : -c.ThrustNewtons * throttle / (c.ThrustIsp * 9.80665d);
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
        var extra = new System.Collections.Generic.List<double>();
        if (c.Thrust)
        {
            // A 600 s burn can fit entirely between two per-orbit checkpoints.
            // Add the thrust/ramp edges (and the middle of the burn) so the max
            // metric actually samples the event, not only the coast after it.
            foreach (double edge in StudyShipRefBreakpoints(c))
            {
                if (edge > StudyShipT0 + 1e-6d && edge < StudyShipT0 + c.Seconds - 1e-6d)
                {
                    extra.Add(edge);
                }
            }

            extra.Add(c.ThrustStart + (0.5d * c.ThrustSeconds));
        }

        if (c.SeamAwareReference)
        {
            // The lunar flyby reference is split at baked-ephemeris seams; put
            // checkpoints there too, otherwise the graded trajectory can jump
            // over the kink the reference is explicitly handling.
            foreach (double seam in StudyShipRefSeamTimes(c))
            {
                if (seam > StudyShipT0 + 1e-6d && seam < StudyShipT0 + c.Seconds - 1e-6d)
                {
                    extra.Add(seam);
                }
            }
        }

        if (extra.Count > 0)
        {
            var merged = new System.Collections.Generic.List<double>(times);
            merged.AddRange(extra);
            merged.Sort();
            var unique = new System.Collections.Generic.List<double>();
            foreach (double value in merged)
            {
                if (unique.Count == 0 || value - unique[unique.Count - 1] > 1e-6d)
                {
                    unique.Add(value);
                }
            }

            times = unique.ToArray();
        }

        return times;
    }

    private static double[] StudyShipMatrixStops(
        StudyShipMatrixCase c, double[] checkpoints)
    {
        double[] edges = StudyShipRefBreakpoints(c);
        return checkpoints == null
            ? edges
            : StudyShipRefMergeEdges(edges, checkpoints);
    }

    private static int StudyShipMatrixRecordCheckpoints(
        double time, double[] checkpoints, int index,
        Vector3d position, Vector3d velocity,
        Vector3d[] sampleP, Vector3d[] sampleV)
    {
        if (checkpoints == null || sampleP == null || sampleV == null)
        {
            return index;
        }

        while (index < checkpoints.Length
            && Math.Abs(checkpoints[index] - time) <= 1e-9d)
        {
            sampleP[index] = position;
            sampleV[index] = velocity;
            index++;
        }

        return index;
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
            // This is the periapsis timescale used for step/reference resolution,
            // not the orbital period: an eccentric orbit needs periapsis resolution,
            // and the actual period would make the HEO reference far too coarse.
            Tau = StudyShipMatrixTau(rp, mu),
            StepScale = stepScale,
            // Characteristic orbit size: used for err_rel and the report diagnostics,
            // never for the (absolute) target values. Without this every orbit case
            // inherited the 1 m field default.
            ScaleLength = Math.Max(rp, ra),
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
            // The golden stores a world-frame dd endpoint projected to the local
            // frame. That subtraction of two ~1.5e11 m doubles has its own ulp,
            // which the old golden did not include in self_error_m. Apply it at
            // read time so the tracked golden does not need to be rebuilt.
            OrbitingBody goldenCentral = c.Sys.AllBodies[c.Central];
            c.Sys.EvaluateBodyState(goldenCentral, StudyShipT0 + c.Seconds,
                out Vector3d goldenBodyP, out _);
            Vector3d goldenRel = new Vector3d(golden.PRel[0], golden.PRel[1], golden.PRel[2]);
            double projectionFloor = ToleranceReference.FloatingPointFloor
                * Math.Max(1d, Math.Max(goldenBodyP.Magnitude,
                    (goldenBodyP + goldenRel).Magnitude));
            selfError = Math.Max(golden.SelfErrorM, projectionFloor);
            refError = selfError;
            ratio = double.PositiveInfinity;
            kind = "Taylor-dd K" + golden.OrderA.ToString(CultureInfo.InvariantCulture)
                + "/K" + golden.OrderB.ToString(CultureInfo.InvariantCulture)
                + " (golden " + Path.GetFileName(StudyShipDdGoldenPath())
                + ", self=" + golden.SelfErrorM.ToString("E2", CultureInfo.InvariantCulture)
                + " m, projection floor=" + projectionFloor.ToString("E2", CultureInfo.InvariantCulture)
                + " m)";
            Console.WriteLine("golden dd projection floor " + c.Label + ": "
                + projectionFloor.ToString("E2", CultureInfo.InvariantCulture)
                + " m (world->local); effective self="
                + selfError.ToString("E2", CultureInfo.InvariantCulture) + " m");
            return StudyShipMatrixRefFromState(c,
                goldenRel,
                new Vector3d(golden.VRel[0], golden.VRel[1], golden.VRel[2]), c.Mass0);
        }

        double h = c.ReferenceStepSeconds > 0d
            ? c.ReferenceStepSeconds
            : Math.Max(2.5d, Math.Min(600d, c.Tau / 1000d));
        // Same local force and frame as the certified reference. This is a local
        // Y6C consistency check, not a true dd-force reference: the force is
        // evaluated in plain double. A world-frame DD run is NOT a valid
        // cross-check here: its body positions are doubles, and the ~1e-5 m
        // absolute-coordinate noise makes its self-convergence ratio ~1 even at a
        // 5 s step, which is the size of the reported 1e-3..8.5 m "formulation
        // difference".
        var r1 = StudyShipLocalY6CDdConsistency(
            c.Sys, c.Central, c.P0, c.V0, StudyShipT0, c.Seconds / 86400d, h);
        var r2 = StudyShipLocalY6CDdConsistency(
            c.Sys, c.Central, c.P0, c.V0, StudyShipT0, c.Seconds / 86400d, h * 0.5d);
        selfError = (r1.p - r2.p).Magnitude;
        var r3 = StudyShipLocalY6CDdConsistency(
            c.Sys, c.Central, c.P0, c.V0, StudyShipT0, c.Seconds / 86400d, h * 0.25d);
        double d23 = (r2.p - r3.p).Magnitude;
        // A bit-identical triple is not a zero-uncertainty measurement. The
        // double-double state still has the double projection/roundoff floor of
        // the local state; without it a zero d23 makes ratio=inf and refError=0.
        double ddRoundoff = ToleranceReference.FloatingPointFloor
            * Math.Max(1d, Math.Max(r2.p.Magnitude, r3.p.Magnitude));
        selfError = Math.Max(selfError, ddRoundoff);
        d23 = Math.Max(d23, ddRoundoff);
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
        kind = "local Y6C-dd consistency check h=" + h.ToString("G6", CultureInfo.InvariantCulture)
            + (asymptotic
                ? " s (est err " + refError.ToString("E2", CultureInfo.InvariantCulture) + " m)"
                : " s (non-asymptotic)");

        return StudyShipMatrixRefFromState(c, r2.p, r2.v, c.Mass0);
    }

    private static StudyShipMatrixRefs StudyShipMatrixBuildReferences(StudyShipMatrixCase c)
    {
        var refs = new StudyShipMatrixRefs();
        if (c.SeamAwareReference)
        {
            var bakeInfo = new System.Text.StringBuilder();
            foreach (OrbitingBody body in c.Sys.AllBodies)
            {
                BakedEphemeris baked = body.Baked;
                bakeInfo.Append(' ').Append(body.Name).Append('=')
                    .Append(baked == null ? "none"
                        : baked.SegmentCount.ToString(CultureInfo.InvariantCulture) + "/"
                          + baked.SegmentLengthSeconds.ToString("F0", CultureInfo.InvariantCulture));
            }

            Console.WriteLine("INFO seam-aware reference " + c.Label + ": "
                + StudyShipRefSeamTimes(c).Length.ToString(CultureInfo.InvariantCulture)
                + " baked-ephemeris seam time(s) inside the window;" + bakeInfo);
        }

        refs.CheckpointTimes = StudyShipMatrixCheckpointTimes(c);
        refs.CheckpointP = new Vector3d[refs.CheckpointTimes.Length];
        refs.CheckpointV = new Vector3d[refs.CheckpointTimes.Length];
        StudyShipCertifiedRef cert = StudyShipCertifiedReference(
            c, c.Central == 1 && c.Seconds > 30d * 86400d ? 8d : 24d,
            5, refs.CheckpointTimes, refs.CheckpointP, refs.CheckpointV);
        if (!cert.Valid && c.AllowUnratioedResolution
            && double.IsFinite(cert.FinestDelta)
            && cert.FinestDelta <= 0.01d * Math.Max(c.AccuracyTarget, 0.01d))
        {
            cert.Valid = true;
            cert.Asymptotic = false;
            cert.Resolved = true;
            cert.PositionError = cert.FinestDelta;
            cert.VelocityError = 0d;
            cert.Detail += " RESOLVED WITHOUT RATIO: finest d="
                + cert.FinestDelta.ToString("E3", CultureInfo.InvariantCulture)
                + " m <= 0.01*target; no Richardson pair (stiff/round-off plateau)";
        }

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

            // The golden is an independent endpoint reference. The local DOP853
            // reference is still used for trajectory samples (the golden has no
            // per-checkpoint states), but its own error estimate must not hide the
            // independent disagreement. Raise the certified floor to the
            // formulation difference so a 1 m claim cannot be made on top of an
            // 8 m model/formulation uncertainty.
            if (c.UseTaylorDdReference && double.IsFinite(refs.Formulation))
            {
                if (c.LocalReferenceOnly)
                {
                    // The game integrates the local formulation. Grade against the
                    // local certified reference, but keep the independent DD
                    // disagreement visible in the reference note. This is how the
                    // long-LEO sweep can resolve 1 m without pretending that the
                    // 8.5 m world-vs-local formulation gap does not exist.
                    refs.LocalKind += " [local-reference floor; independent dd endpoint differs by "
                        + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture)
                        + " m, not folded into the floor]";
                }
                else
                {
                    refs.LocalError = Math.Max(
                        double.IsFinite(refs.LocalError) ? refs.LocalError : 0d,
                        refs.Formulation);
                    refs.LocalKind += " + independent dd endpoint floor "
                        + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture) + " m";
                    refs.LocalAsymptotic = refs.LocalAsymptotic || refs.LocalError > 0d;
                }
            }
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
            double[] stops = StudyShipMatrixStops(c, checkpoints);
            int sampleIndex = 0;
            for (int k = 0; k < stops.Length; k++)
            {
                double stop = stops[k];
                double span = stop - t;
                if (span <= 0d)
                {
                    continue;
                }

                SpacecraftIntegrationResult r = dop853
                    ? SpacecraftOrbitIntegrator.StepForwardDop853(
                        state, t, span, acc.Evaluate, posTol, velTol, massTol, rtol)
                    : SpacecraftOrbitIntegrator.StepForward(
                        state, t, span, acc.Evaluate, posTol, velTol, massTol, rtol);
                state = r.State;
                t = stop;
                sampleIndex = StudyShipMatrixRecordCheckpoints(
                    t, checkpoints, sampleIndex, state.Position, state.Velocity,
                    run.SamplesP, run.SamplesV);
            }

            sw.Stop();
            run.P = state.Position;
            run.V = state.Velocity;
            run.M = state.Mass;
            run.Calls = acc.Calls;
            run.RawCalls = acc.Calls;
            run.SamplesRecorded = sampleIndex;
            run.Ms = sw.Elapsed.TotalMilliseconds;
            return run;
        });
    }

    private static StudyShipRun StudyShipMatrixWorld(
        StudyShipMatrixCase c, bool dop853,
        double posTol = 1e-4d, double velTol = 1e-8d, double massTol = 1e-4d,
        double rtol = 1e-11d, double chunkSeconds = 600d, bool useToleranceFrame = true,
        bool splitThrust = false, double[] checkpoints = null,
        double thrustChunkSeconds = 0d)
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
                    if (c.Thrust && thrustChunkSeconds > 0d
                        && StudyShipMatrixThrottle(c, from) > 0d)
                    {
                        // The game uses VesselStep.FlightChunkSeconds during thrust,
                        // but DefaultMaxStepSize on a coast. Reproduce that split.
                        chunk = Math.Min(chunk, thrustChunkSeconds);
                    }
                    phys.Step(ship, from, chunk);
                    from += chunk;
                }

                FlushSamples(from);
            }

            if (splitThrust)
            {
                // Stop exactly at every edge/checkpoint; do NOT add artificial
                // tiny steps around a thrust edge. The old 1e-6 edge step was 34x
                // the reference's own h*1e-9 edge and made the burn-split row 33x
                // worse than the same run without it (measured: 1.56e-2 m vs
                // 4.66e-4 m). The checkpoints already force a step boundary at
                // the thrust edges, so the extra integration was harmful.
                for (int segment = 0; segment < edges.Length - 1; segment++)
                {
                    double segmentEnd = edges[segment + 1];
                    StepRange(t, segmentEnd);
                    t = segmentEnd;
                }
            }
            else
            {
                // Keep the historical horizon-edge sub-steps for the unsplit
                // production row: their removal changed LEO sun-synchronous by
                // 3.5x, and that row is the production reference path here.
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
            }

            sw.Stop();
            c.Sys.EvaluateBodyState(central, StudyShipT0 + c.Seconds,
                out Vector3d bp1, out Vector3d bv1);
            bool localToCentralEnd = ship.HasLocalState && ReferenceEquals(ship.LocalBody, central);
            run.P = localToCentralEnd ? ship.LocalPosition : ship.Position - bp1;
            run.V = localToCentralEnd ? ship.LocalVelocity : ship.Velocity - bv1;
            run.M = ship.Mass;
            run.SamplesRecorded = sample;
            // The local-frame production RHS evaluates the gravity field three
            // times per derivative: (1) the CachedGravitySource sum seen by
            // CountingSource, (2) the absolute term SpacecraftPhysics subtracts in
            // EvaluateLocalDerivative, and (3) the local relative field it adds.
            // All three are real StarSystem acceleration sums, so the x3 is the
            // measured cost of the production-local frame, not a fudge factor.
            // Without it every WH/production ratio and the cost Pareto flatter
            // production by 3x. A genuine absolute-frame row (useToleranceFrame
            // false) would pay only (1); no such row is built here. RawCalls keeps
            // the unweighted count; Calls is the RHS-equivalent cost.
            run.RawCalls = counting.Calls;
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
                RawCalls = r.Calls,
                Steps = r.Steps,
                Ms = r.Ms,
                SamplesRecorded = pSamples?.Count ?? 0,
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
            s[6] += dt * StudyShipMatrixMassFlow(acc.Case, tBase + (0.5d * dt));
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
            s[6] += dt * StudyShipMatrixMassFlow(
                acc.Case, tBase + (xi * h) + (0.5d * dt));
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
            s[6] += dt * StudyShipMatrixMassFlow(
                acc.Case, tBase + ((xi + chi) * h) + (0.5d * dt));
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
            // The fourth drift starts at 1 - chi - xi (after the first three
            // drifts), not at 1 - xi; 1 - xi is its end, used by the kick below.
            s[6] += dt * StudyShipMatrixMassFlow(
                acc.Case, tBase + ((1d - chi - xi) * h) + (0.5d * dt));
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
            s[6] += dt * StudyShipMatrixMassFlow(
                acc.Case, tBase + ((1d - xi) * h) + (0.5d * dt));
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

                Add(s, sl, 6, dt * StudyShipMatrixMassFlow(
                    acc.Case, tBase + (cw[k] * h) + (0.5d * dt)));
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
        s[6] += h * StudyShipMatrixMassFlow(acc.Case, end - (0.5d * h));
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
            int sampleIndex = 0;
            var sw = Stopwatch.StartNew();
            double t = StudyShipT0;
            double[] stops = StudyShipMatrixStops(c, checkpoints);
            for (int k = 0; k < stops.Length; k++)
            {
                double stop = stops[k];
                double span = stop - t;
                if (span > 0d)
                {
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
                }

                sampleIndex = StudyShipMatrixRecordCheckpoints(
                    t, checkpoints, sampleIndex,
                    new Vector3d(s[0], s[1], s[2]),
                    new Vector3d(s[3], s[4], s[5]),
                    run.SamplesP, run.SamplesV);
            }

            sw.Stop();
            run.P = new Vector3d(s[0], s[1], s[2]);
            run.V = new Vector3d(s[3], s[4], s[5]);
            run.M = s[6];
            run.Calls = calls;
            run.RawCalls = calls;
            run.Steps = steps;
            run.SamplesRecorded = sampleIndex;
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
            RawCalls = run.RawCalls > 0L ? run.RawCalls : run.Calls,
            Calls = run.Calls,
            // TaylorShip.Calls counts jet-derivative orders (one full K12 jet is
            // 12 calls), so a raw call comparison flatters it by ~12x before the
            // per-call cost difference is even counted. The x12 is an explicit
            // assumption, not a calibrated per-call price: Taylor is excluded from
            // every cost claim and from the Pareto/scoreboard rankings.
            Cost = method == "Taylor" ? run.Calls * 12L : run.Calls,
            Steps = run.Steps,
            Ms = run.Ms,
            Reps = run.Reps,
            Note = note,
            P = run.P,
            V = run.V,
            Mass = run.M,
            SamplesP = run.SamplesP,
            // Actual samples produced by this row, not the reference's checkpoint
            // count: a lost sample is visible in the CSV and fails the max metric.
            SampleCount = run.SamplesRecorded
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
            row.DEnergyDrift = double.NaN;
            return row;
        }

        StudyShipMatrixRef reference = refs.Local;
        row.DpFinal = (run.P - reference.P).Magnitude;
        row.DvFinal = (run.V - reference.V).Magnitude;
        int expectedSamples = refs.CheckpointP?.Length ?? 0;
        bool samplesComplete = expectedSamples > 0
            && run.SamplesP != null && run.SamplesV != null
            && run.SamplesP.Length == expectedSamples
            && run.SamplesV.Length == expectedSamples
            && run.SamplesRecorded == expectedSamples;
        if (!samplesComplete)
        {
            // A lost checkpoint must not fall back to the final point: that would
            // grade the prefix that happened to be produced and can turn a broken
            // run into a winner. Such a row is non-finite and cannot pass or rank.
            row.Finite = false;
            row.Dp = double.NaN;
            row.Dv = double.NaN;
            row.Da = double.NaN;
            row.De = double.NaN;
            row.DEnergy = double.NaN;
            row.DEnergyDrift = double.NaN;
            row.Note = ((note ?? "") + " samples lost ("
                + run.SamplesRecorded.ToString(CultureInfo.InvariantCulture)
                + " of " + expectedSamples.ToString(CultureInfo.InvariantCulture) + ")").Trim();
            return row;
        }

        // The ranked metric is the worst point of the whole trajectory: a final
        // point alone rewards a lucky phase on long runs.
        row.Dp = Math.Max(StudyShipMaxSample(run.SamplesP, refs.CheckpointP), row.DpFinal);
        row.Dv = Math.Max(StudyShipMaxSample(run.SamplesV, refs.CheckpointV), row.DvFinal);
        OrbitalElements el = OrbitalElements.FromState(
            run.P, run.V, c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter());
        row.Da = el.IsValid && reference.Elements.IsValid
            ? Math.Abs(el.SemiMajorAxis - reference.Elements.SemiMajorAxis)
            : double.NaN;
        row.De = el.IsValid && reference.Elements.IsValid
            ? Math.Abs(el.Eccentricity - reference.Elements.Eccentricity)
            : double.NaN;
        double mu = c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter();
        double energy = StudyShipMatrixEnergy(run.P, run.V, mu);
        double refEnergy = StudyShipMatrixEnergy(reference.P, reference.V, mu);
        double initialEnergy = StudyShipMatrixEnergy(c.P0, c.V0, mu);
        row.DEnergy = Math.Abs(energy - refEnergy) / Math.Max(Math.Abs(refEnergy), 1e-300d);
        row.DEnergyDrift = Math.Abs((energy - initialEnergy) / Math.Max(Math.Abs(initialEnergy), 1e-300d));
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
        foreach (string method in StudyShipMatrixFixedMethods)
        {
            foreach (double h in grid)
            {
                Add(method, H(h), StudyShipFrameLocal,
                    () => StudyShipMatrixFixed(c, method, h, checkpoints), "");
            }
        }

        // Absolute-tolerance sweep with rtol=0. A fixed relative tolerance of
        // 1e-12 is 6.8e-6 m at a 6.8e6 m LEO state, which would dominate every
        // atol below 1e-6 and make the three rows the same run (the same mistake
        // StudyShipMatrixOrderAndTolerance explicitly avoids).
        foreach (bool dop853 in new[] { true, false })
        {
            // Relative-tolerance sweep. An absolute atol below the state's
            // roundoff floor made the 1e-6 and 1e-8 rows the same run (and drove
            // DOPRI5 to MinStepSize on the heliocentric cases). With atol=0 the
            // scale is rtol*|state|: 7e-6/7e-4/7e-2 m at LEO, 0.15/15/1500 m at
            // 1 AU, so the three rows are genuinely different and affordable.
            foreach (double rtol in new[] { 1e-8d, 1e-10d, 1e-12d })
            {
                string cfg = "rtol=" + rtol.ToString("G3", CultureInfo.InvariantCulture);
                Add(dop853 ? "DOP853" : "DOPRI5", cfg, "local",
                    () => StudyShipMatrixAdaptive(
                        c, dop853, 0d, 0d, 0d, rtol, checkpoints),
                    "");
            }
        }

        // The production path keeps the ship state in the central body's local
        // frame and uses the system only for tolerance scaling. Its frame is named
        // production-local, not "world": the state handed to the integrator is
        // converted back to world, but every tolerance and every step decision is
        // made on the local relative state.
        string productionCfg = StudyShipFrameProductionLocal + " "
            + c.WorldChunkSeconds.ToString("G", CultureInfo.InvariantCulture) + " s";
        foreach (bool dop853 in new[] { true, false })
        {
            Add(dop853 ? "DOP853" : "DOPRI5", productionCfg, StudyShipFrameProductionLocal,
                () => StudyShipMatrixWorld(
                    c, dop853, chunkSeconds: c.WorldChunkSeconds, checkpoints: checkpoints),
                "production");
        }

        if (c.Thrust)
        {
            Add("DOP853", StudyShipFrameProductionLocal + " burn-split", StudyShipFrameProductionLocal,
                () => StudyShipMatrixWorld(
                    c, true, chunkSeconds: c.WorldChunkSeconds, splitThrust: true,
                    checkpoints: checkpoints),
                "burn-split");
        }

        if (c.Thrust && Math.Abs(c.WorldChunkSeconds - 600d) > 1e-9d)
        {
            // Diagnostic only: this row changes the chunk length, not the
            // integrator, and is not the production path. It must never carry the
            // "production chunk" accuracy gate.
            foreach (bool dop853 in new[] { true, false })
            {
                Add(dop853 ? "DOP853" : "DOPRI5",
                    StudyShipFrameProductionLocal + " 600 s (trial-mass diagnostic)",
                    StudyShipFrameProductionLocal,
                    () => StudyShipMatrixWorld(
                        c, dop853, chunkSeconds: 600d, checkpoints: checkpoints),
                    "trial-mass diagnostic");
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
        Console.WriteLine("frame method      config          dp_max(m)   dp_final(m) dv_max(m/s) da(m)       de          dE/E(drift) calls      steps      ms      reps");
        Console.WriteLine("calls are RHS-equivalent cost (production-local is x3-weighted; raw_calls is in the CSV); Taylor calls are jet-derivative orders (cost = calls x 12, an assumption, excluded from claims). ms is sequential min-of-up-to-3; only 3-rep rows compete on ms.");
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
                r.DEnergyDrift,
                r.Calls.ToString("N0", CultureInfo.InvariantCulture),
                r.Steps < 0L ? "-" : r.Steps.ToString("N0", CultureInfo.InvariantCulture),
                r.Ms, r.Reps, note.Length > 0 ? " " + note : ""));
        }

        if (double.IsFinite(floor))
        {
            // Ranking below the formulation floor would be noise, so raise the
            // Pareto target to the floor instead of producing an empty set.
            double target = Math.Max(c.AccuracyTarget, floor);
            StudyShipMatrixPareto(rows, StudyShipFrameLocal, target, floor);
            StudyShipMatrixPareto(rows, StudyShipFrameProductionLocal, target, floor);
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

    /// <summary>
    /// A row may enter a ranking. Taylor is excluded (its x12 cost is not
    /// calibrated). In the production-local frame only the actual production
    /// path is ranked: burn-split and trial-mass rows are diagnostics, not the
    /// production path, and must not win a production claim.
    /// </summary>
    private static bool StudyShipMatrixRankable(StudyShipMatrixRow r, string frame)
    {
        if (r.Method == "Taylor")
        {
            return false;
        }

        if (frame == StudyShipFrameProductionLocal && r.Note != "production")
        {
            return false;
        }

        return true;
    }

    private static void StudyShipMatrixPareto(
        List<StudyShipMatrixRow> rows, string frame, double target, double floor)
    {
        Check(double.IsFinite(floor), "matrix Pareto certified floor " + frame,
            double.IsFinite(floor)
                ? floor.ToString("E3", CultureInfo.InvariantCulture) + " m"
                : "uncertified reference floor; Pareto must not run");
        if (!double.IsFinite(floor))
        {
            return;
        }

        var valid = new List<StudyShipMatrixRow>();
        foreach (StudyShipMatrixRow r in rows)
        {
            double ranked = StudyShipMatrixRanked(r, floor);
            // Taylor's "calls" are jet-derivative orders, not RHS evaluations; the
            // x12 cost is a rough proxy and not calibrated against the measured
            // per-call work. Keep its row in the table, but do not let an
            // uncalibrated cost model decide the production Pareto.
            if (r.Frame == frame && StudyShipMatrixRankable(r, frame) && r.Finite
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
        // The "best accuracy" row is selected by the raw measured Dp, not by
        // max(Dp, floor): below the floor every row is tied and the old selection
        // silently reported the cheapest floor-bound row as the most accurate.
        StudyShipMatrixRow accurate = valid[0];
        foreach (StudyShipMatrixRow r in valid)
        {
            if (r.Dp < accurate.Dp
                || (r.Dp == accurate.Dp && r.Cost < accurate.Cost))
            {
                accurate = r;
            }
        }

        Console.WriteLine("  best measured dp: " + accurate.Method + "/" + accurate.Config
            + " dp=" + accurate.Dp.ToString("E2", CultureInfo.InvariantCulture)
            + (accurate.Dp < floor ? " (below reference floor; not certified)" : "")
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
        Dictionary<string, double> floors, HashSet<string> certified)
    {
        var uncertified = new List<string>();
        var rankedScenarios = new HashSet<string>(StringComparer.Ordinal);
        foreach (string frame in new[] { StudyShipFrameLocal, StudyShipFrameProductionLocal })
        {
            foreach (double target in new[] { 1d, 10d, 100d })
            {
                var winsCost = new Dictionary<string, int>();
                var winsMs = new Dictionary<string, int>();
                var notClaimed = new List<string>();
                var seen = new HashSet<string>();
                for (int i = 0; i < all.Count; i++)
                {
                    string scenario = labels[i];
                    if (!seen.Add(scenario))
                    {
                        continue;
                    }

                    floors.TryGetValue(scenario, out double floor);
                    if (!certified.Contains(scenario) || !double.IsFinite(floor))
                    {
                        // No certified reference floor: Dp is measured against an
                        // uncertified reference, so this scenario cannot produce an
                        // accuracy win. Report it separately and rank nothing.
                        if (!uncertified.Contains(scenario))
                        {
                            uncertified.Add(scenario);
                        }

                        continue;
                    }

                    if (floor > target)
                    {
                        // The reference itself cannot resolve the requested target.
                        // Ranking at the floor would label a floor-bound row as a
                        // "<= target" win, which the comparisons section explicitly
                        // refuses to claim.
                        notClaimed.Add(scenario);
                        continue;
                    }

                    double scenarioTarget = target;
                    StudyShipMatrixRow bestCost = null;
                    StudyShipMatrixRow bestMs = null;
                    for (int j = 0; j < all.Count; j++)
                    {
                        StudyShipMatrixRow q = all[j];
                        double qp = StudyShipMatrixRanked(q, floor);
                        if (labels[j] != scenario || q.Frame != frame
                            || !StudyShipMatrixRankable(q, frame)
                            || !q.Finite || !double.IsFinite(qp) || q.Cost <= 0L
                            || qp > scenarioTarget)
                        {
                            continue;
                        }

                        if (bestCost == null || q.Cost < bestCost.Cost
                            || (q.Cost == bestCost.Cost
                                && qp < StudyShipMatrixRanked(bestCost, floor)))
                        {
                            bestCost = q;
                        }

                        // Only min-of-3 rows are comparable with each other. A
                        // single-rep row is still in the table, but its ms is not
                        // used to declare a wall-clock win.
                        if (q.Reps >= 3
                            && (bestMs == null || q.Ms < bestMs.Ms
                                || (q.Ms == bestMs.Ms
                                    && qp < StudyShipMatrixRanked(bestMs, floor))))
                        {
                            bestMs = q;
                        }
                    }

                    if (bestCost != null)
                    {
                        string key = bestCost.Method + "/" + bestCost.Config;
                        winsCost[key] = winsCost.TryGetValue(key, out int n) ? n + 1 : 1;
                        rankedScenarios.Add(scenario);
                    }

                    if (bestMs != null)
                    {
                        string key = bestMs.Method + "/" + bestMs.Config;
                        winsMs[key] = winsMs.TryGetValue(key, out int n) ? n + 1 : 1;
                        rankedScenarios.Add(scenario);
                    }
                }

                if (notClaimed.Count > 0)
                {
                    Console.WriteLine(frame + " <= "
                        + target.ToString("G", CultureInfo.InvariantCulture)
                        + " m not claimed (reference floor above target): "
                        + string.Join(", ", notClaimed));
                }

                Console.Write(frame + " <= " + target.ToString("G", CultureInfo.InvariantCulture)
                    + " m wins by RHS-eq cost:");
                StudyShipMatrixPrintWins(winsCost);
                Console.Write(frame + " <= " + target.ToString("G", CultureInfo.InvariantCulture)
                    + " m wins by ms (3-rep rows only):");
                StudyShipMatrixPrintWins(winsMs);
            }
        }

        foreach (string scenario in uncertified)
        {
            Check(!rankedScenarios.Contains(scenario),
                "matrix uncertified not ranked " + scenario,
                rankedScenarios.Contains(scenario)
                    ? "scenario contributed an accuracy win without a certified floor"
                    : "accuracy wins suppressed");
        }

        if (uncertified.Count > 0)
        {
            Console.WriteLine("uncertified, not ranked: " + string.Join(", ", uncertified)
                + " (no finite certified reference floor; accuracy wins suppressed)");
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

    private sealed class StudyShipMatrixInterpPoint
    {
        public StudyShipMatrixRow Row;
        public double Error;
    }

    private sealed class StudyShipMatrixInterpFit
    {
        public string Key;
        public List<StudyShipMatrixInterpPoint> Segment;
        public int Total;
        public int Kept;
        public int Dropped;
        public double MinError;
        public double MaxError;
        public bool SingleRow;
        public long SingleCost;
        public double SingleError;
        public bool Interpolated;
        public double Cost;
        public double Order;
    }

    private static string StudyShipMatrixInterpFamily(StudyShipMatrixRow r)
    {
        if (r.Config != null && r.Config.StartsWith("h=", StringComparison.Ordinal))
        {
            return r.Method + "/h";
        }

        if (r.Config != null && r.Config.StartsWith("atol=", StringComparison.Ordinal))
        {
            return r.Method + "/atol";
        }

        if (r.Config != null && r.Config.StartsWith("rtol=", StringComparison.Ordinal))
        {
            return r.Method + "/rtol";
        }

        if (r.Config != null && r.Config.StartsWith("tol=", StringComparison.Ordinal))
        {
            return r.Method + "/tol";
        }

        return r.Method + "/" + r.Config;
    }

    private static List<StudyShipMatrixInterpFit> StudyShipMatrixInterpFits(
        List<StudyShipMatrixRow> all, List<string> labels,
        string scenario, string frame, double floor)
    {
        var groups = new Dictionary<string, List<StudyShipMatrixInterpPoint>>();
        var keys = new List<string>();
        for (int j = 0; j < all.Count; j++)
        {
            StudyShipMatrixRow q = all[j];
            if (labels[j] != scenario || q.Frame != frame
                || !StudyShipMatrixRankable(q, frame)
                || !q.Finite || q.Cost <= 0L)
            {
                continue;
            }

            double error = StudyShipMatrixRanked(q, floor);
            if (!double.IsFinite(error) || error <= 0d)
            {
                continue;
            }

            string key = StudyShipMatrixInterpFamily(q);
            if (!groups.TryGetValue(key, out List<StudyShipMatrixInterpPoint> points))
            {
                points = new List<StudyShipMatrixInterpPoint>();
                groups.Add(key, points);
                keys.Add(key);
            }

            points.Add(new StudyShipMatrixInterpPoint { Row = q, Error = error });
        }

        keys.Sort(StringComparer.Ordinal);
        var fits = new List<StudyShipMatrixInterpFit>(keys.Count);
        foreach (string key in keys)
        {
            fits.Add(StudyShipMatrixInterpFitMake(key, groups[key]));
        }

        return fits;
    }

    private static StudyShipMatrixInterpFit StudyShipMatrixInterpFitMake(
        string key, List<StudyShipMatrixInterpPoint> points)
    {
        points.Sort((a, b) =>
        {
            int byCost = a.Row.Cost.CompareTo(b.Row.Cost);
            return byCost != 0 ? byCost : a.Error.CompareTo(b.Error);
        });

        var unique = new List<StudyShipMatrixInterpPoint>(points.Count);
        foreach (StudyShipMatrixInterpPoint p in points)
        {
            if (unique.Count > 0 && unique[unique.Count - 1].Row.Cost == p.Row.Cost)
            {
                continue;
            }

            unique.Add(p);
        }

        int bestStart = 0;
        int bestLength = 1;
        int runStart = 0;
        for (int i = 1; i < unique.Count; i++)
        {
            if (unique[i].Error < unique[i - 1].Error)
            {
                int length = i - runStart + 1;
                if (length > bestLength)
                {
                    bestLength = length;
                    bestStart = runStart;
                }
            }
            else
            {
                runStart = i;
            }
        }

        var fit = new StudyShipMatrixInterpFit
        {
            Key = key,
            Total = points.Count,
            Kept = bestLength,
            Dropped = points.Count - bestLength,
            Segment = unique.GetRange(bestStart, bestLength),
            SingleRow = unique.Count == 1,
            Cost = double.NaN,
            Order = double.NaN
        };
        fit.MaxError = fit.Segment[0].Error;
        fit.MinError = fit.Segment[fit.Segment.Count - 1].Error;
        if (fit.SingleRow)
        {
            fit.SingleCost = fit.Segment[0].Row.Cost;
            fit.SingleError = fit.Segment[0].Error;
        }

        return fit;
    }

    private static void StudyShipMatrixInterpEvaluate(
        StudyShipMatrixInterpFit fit, double target, double floor)
    {
        fit.Interpolated = false;
        fit.Cost = double.NaN;
        fit.Order = double.NaN;
        double raised = Math.Max(target, double.IsFinite(floor) ? floor : target);
        for (int i = 0; i + 1 < fit.Segment.Count; i++)
        {
            double eWorse = fit.Segment[i].Error;
            double eBetter = fit.Segment[i + 1].Error;
            if (eWorse <= eBetter || raised > eWorse || raised < eBetter)
            {
                continue;
            }

            long cWorse = fit.Segment[i].Row.Cost;
            long cBetter = fit.Segment[i + 1].Row.Cost;
            double t = (Math.Log(eWorse) - Math.Log(raised))
                / (Math.Log(eWorse) - Math.Log(eBetter));
            fit.Cost = Math.Exp(Math.Log(cWorse)
                + t * (Math.Log(cBetter) - Math.Log(cWorse)));
            double slope = (Math.Log(cBetter) - Math.Log(cWorse))
                / (Math.Log(eBetter) - Math.Log(eWorse));
            fit.Order = slope < 0d ? -1d / slope : double.NaN;
            fit.Interpolated = true;
            return;
        }
    }

    private static string StudyShipMatrixInterpDescribe(StudyShipMatrixInterpFit fit)
    {
        if (fit.Interpolated)
        {
            return fit.Key + "=" + fit.Cost.ToString("N0", CultureInfo.InvariantCulture)
                + " (order " + fit.Order.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }

        if (fit.SingleRow)
        {
            return fit.Key + "=" + fit.SingleCost.ToString("N0", CultureInfo.InvariantCulture)
                + " (measured, single row, err="
                + fit.SingleError.ToString("E3", CultureInfo.InvariantCulture)
                + ", not interpolated)";
        }

        string dropped = fit.Dropped > 0
            ? "; dropped " + fit.Dropped.ToString(CultureInfo.InvariantCulture)
              + " of " + fit.Total.ToString(CultureInfo.InvariantCulture) + " non-monotone"
            : "";
        string pair = fit.Kept < 2 ? "; no monotone pair" : "";
        return fit.Key + "=out of range (min err="
            + fit.MinError.ToString("E3", CultureInfo.InvariantCulture)
            + ", max err=" + fit.MaxError.ToString("E3", CultureInfo.InvariantCulture)
            + dropped + pair + ")";
    }

    private static StudyShipMatrixRow StudyShipMatrixInterpDiscreteBest(
        List<StudyShipMatrixRow> all, List<string> labels,
        string scenario, string frame, double floor, double target)
    {
        StudyShipMatrixRow best = null;
        for (int j = 0; j < all.Count; j++)
        {
            StudyShipMatrixRow q = all[j];
            double qp = StudyShipMatrixRanked(q, floor);
            if (labels[j] != scenario || q.Frame != frame
                || !StudyShipMatrixRankable(q, frame)
                || !q.Finite || !double.IsFinite(qp) || q.Cost <= 0L || qp > target)
            {
                continue;
            }

            if (best == null || q.Cost < best.Cost
                || (q.Cost == best.Cost && qp < StudyShipMatrixRanked(best, floor)))
            {
                best = q;
            }
        }

        return best;
    }

    private static StudyShipMatrixRow StudyShipMatrixInterpWinner(
        List<StudyShipMatrixRow> all, List<string> labels,
        List<StudyShipMatrixInterpFit> fits,
        string scenario, string frame, double floor, double target,
        out bool gridLimited, out double headroom)
    {
        gridLimited = false;
        headroom = double.NaN;
        if (!double.IsFinite(floor))
        {
            return null;
        }

        StudyShipMatrixRow discrete = StudyShipMatrixInterpDiscreteBest(
            all, labels, scenario, frame, floor, target);
        if (discrete == null)
        {
            return null;
        }

        headroom = StudyShipMatrixRanked(discrete, floor) / target;
        string family = StudyShipMatrixInterpFamily(discrete);
        foreach (StudyShipMatrixInterpFit fit in fits)
        {
            if (fit.Key == family)
            {
                // The fit's segment is the measured monotone curve the block can
                // interpolate on; a target above it would need extrapolation to a
                // coarser row, so the discrete winner is not comparable.
                gridLimited = fit.MaxError < target;
                break;
            }
        }

        return discrete;
    }

    private static void StudyShipMatrixInterpHeadroom(
        List<StudyShipMatrixRow> all, List<string> labels,
        List<StudyShipMatrixInterpFit> fits,
        string scenario, string frame, double floor, double target,
        double keyTarget, Dictionary<string, List<double>> headroomByFrameTarget)
    {
        StudyShipMatrixRow discrete = StudyShipMatrixInterpWinner(
            all, labels, fits, scenario, frame, floor, target,
            out bool gridLimited, out double headroom);
        if (discrete == null)
        {
            Console.WriteLine("  discrete headroom: " + scenario + ": "
                + (double.IsFinite(floor)
                    ? "no row at or below target"
                    : "not ranked (uncertified reference)"));
            return;
        }

        Console.WriteLine("  discrete headroom: " + scenario + ": "
            + discrete.Method + "/" + discrete.Config
            + " err=" + StudyShipMatrixRanked(discrete, floor).ToString(
                "E3", CultureInfo.InvariantCulture)
            + " m / target'=" + target.ToString("G", CultureInfo.InvariantCulture)
            + " m = " + headroom.ToString("E3", CultureInfo.InvariantCulture)
            + (gridLimited ? " grid-limited" : " in range"));
        if (gridLimited)
        {
            string key = frame + "|" + keyTarget.ToString("R", CultureInfo.InvariantCulture);
            if (!headroomByFrameTarget.TryGetValue(key, out List<double> values))
            {
                values = new List<double>();
                headroomByFrameTarget.Add(key, values);
            }

            values.Add(headroom);
        }
    }

    private static void StudyShipMatrixInterpCompare(
        List<StudyShipMatrixRow> all, List<string> labels,
        Dictionary<string, double> floors, List<string> scenarios,
        Dictionary<string, List<StudyShipMatrixInterpFit>> fitsByScenario,
        string frame, double target,
        Dictionary<string, StudyShipMatrixInterpFit> bestByScenario)
    {
        if (frame == StudyShipFrameProductionLocal)
        {
            Console.WriteLine("interp vs discrete: " + StudyShipFrameProductionLocal + " <= "
                + target.ToString("G", CultureInfo.InvariantCulture)
                + " m: no interpolable families in the production-local frame; see discrete Scoreboard");
        }

        int gridLimited = 0;
        int same = 0;
        int differ = 0;
        int noInterp = 0;
        int noDiscrete = 0;
        int uncertified = 0;
        var details = new List<string>();
        foreach (string scenario in scenarios)
        {
            floors.TryGetValue(scenario, out double floor);
            if (!double.IsFinite(floor))
            {
                uncertified++;
                continue;
            }

            double scenarioTarget = Math.Max(target, floor);
            fitsByScenario.TryGetValue(scenario, out List<StudyShipMatrixInterpFit> fits);
            StudyShipMatrixRow discrete = StudyShipMatrixInterpWinner(
                all, labels, fits, scenario, frame, floor, scenarioTarget,
                out bool isGridLimited, out _);
            if (discrete == null)
            {
                noDiscrete++;
                continue;
            }

            if (isGridLimited)
            {
                gridLimited++;
                continue;
            }

            bestByScenario.TryGetValue(scenario, out StudyShipMatrixInterpFit interp);
            if (interp == null)
            {
                noInterp++;
                continue;
            }

            string discreteFamily = StudyShipMatrixInterpFamily(discrete);
            if (discreteFamily == interp.Key)
            {
                same++;
                continue;
            }

            differ++;
            double percent = 100d * (interp.Cost - discrete.Cost) / discrete.Cost;
            details.Add(scenario + ": discrete " + discrete.Method + "/" + discrete.Config
                + " cost=" + discrete.Cost.ToString("N0", CultureInfo.InvariantCulture)
                + " -> interp " + interp.Key + " cost="
                + interp.Cost.ToString("N0", CultureInfo.InvariantCulture)
                + " (" + (percent >= 0d ? "+" : "")
                + percent.ToString("F1", CultureInfo.InvariantCulture) + "%)");
        }

        Console.WriteLine("interp vs discrete: " + frame + " <= "
            + target.ToString("G", CultureInfo.InvariantCulture) + " m: grid-limited "
            + gridLimited + ", in range same " + same + ", in range differs " + differ
            + ", no interpolated winner " + noInterp + ", no discrete winner " + noDiscrete
            + ", uncertified " + uncertified);
        foreach (string detail in details)
        {
            Console.WriteLine("  " + detail);
        }
    }

    private static void StudyShipMatrixInterpScoreboard(
        List<StudyShipMatrixRow> all, List<string> labels,
        Dictionary<string, double> floors, HashSet<string> certified)
    {
        var scenarios = new List<string>();
        var seen = new HashSet<string>();
        for (int i = 0; i < all.Count; i++)
        {
            if (seen.Add(labels[i]))
            {
                scenarios.Add(labels[i]);
            }
        }

        scenarios.RemoveAll(scenario => !certified.Contains(scenario));

        Console.WriteLine();
        Console.WriteLine("=== interpolated cost at target (log-log, informational; Taylor excluded) ===");
        var totalByFrame = new Dictionary<string, int>();
        var outByFrame = new Dictionary<string, int>();
        var nonMonotoneSeries = new Dictionary<string, int>();
        var nonMonotonePoints = new Dictionary<string, int>();
        var headroomByFrameTarget = new Dictionary<string, List<double>>();

        foreach (string frame in new[] { StudyShipFrameLocal, StudyShipFrameProductionLocal })
        {
            var fitsByScenario = new Dictionary<string, List<StudyShipMatrixInterpFit>>();
            foreach (string scenario in scenarios)
            {
                floors.TryGetValue(scenario, out double floor);
                List<StudyShipMatrixInterpFit> fits =
                    StudyShipMatrixInterpFits(all, labels, scenario, frame, floor);
                fitsByScenario.Add(scenario, fits);
                foreach (StudyShipMatrixInterpFit fit in fits)
                {
                    if (fit.Total > 1 && fit.Dropped > 0)
                    {
                        nonMonotoneSeries[fit.Key] = nonMonotoneSeries.TryGetValue(
                            fit.Key, out int series) ? series + 1 : 1;
                        nonMonotonePoints[fit.Key] = nonMonotonePoints.TryGetValue(
                            fit.Key, out int dropped) ? dropped + fit.Dropped : fit.Dropped;
                    }
                }
            }

            foreach (double target in new[] { 1d, 10d, 100d })
            {
                Console.WriteLine();
                Console.WriteLine("interpolated cost at target: " + frame + " <= "
                    + target.ToString("G", CultureInfo.InvariantCulture) + " m");
                var wins = new Dictionary<string, int>();
                var bestByScenario = new Dictionary<string, StudyShipMatrixInterpFit>();
                foreach (string scenario in scenarios)
                {
                    floors.TryGetValue(scenario, out double floor);
                    double scenarioTarget = Math.Max(
                        target, double.IsFinite(floor) ? floor : target);
                    List<StudyShipMatrixInterpFit> fits = fitsByScenario[scenario];
                    StudyShipMatrixInterpFit best = null;
                    var line = new StringBuilder();
                    line.Append("  ").Append(scenario).Append(':');
                    foreach (StudyShipMatrixInterpFit fit in fits)
                    {
                        StudyShipMatrixInterpEvaluate(fit, scenarioTarget, floor);
                        totalByFrame[frame] = totalByFrame.TryGetValue(
                            frame, out int total) ? total + 1 : 1;
                        if (fit.Interpolated)
                        {
                            if (best == null || fit.Cost < best.Cost
                                || (fit.Cost == best.Cost
                                    && string.CompareOrdinal(fit.Key, best.Key) < 0))
                            {
                                best = fit;
                            }
                        }
                        else
                        {
                            outByFrame[frame] = outByFrame.TryGetValue(
                                frame, out int outCount) ? outCount + 1 : 1;
                        }

                        line.Append(' ').Append(StudyShipMatrixInterpDescribe(fit)).Append(';');
                    }

                    if (fits.Count == 0)
                    {
                        line.Append(" none");
                    }

                    Console.WriteLine(line.ToString());
                    if (best != null)
                    {
                        bestByScenario[scenario] = best;
                        wins[best.Key] = wins.TryGetValue(best.Key, out int n) ? n + 1 : 1;
                    }

                    StudyShipMatrixInterpHeadroom(
                        all, labels, fits, scenario, frame, floor, scenarioTarget,
                        target, headroomByFrameTarget);
                }

                Console.Write("interp " + frame + " <= "
                    + target.ToString("G", CultureInfo.InvariantCulture)
                    + " m wins by interpolated cost:");
                StudyShipMatrixPrintWins(wins);
                StudyShipMatrixInterpCompare(
                    all, labels, floors, scenarios, fitsByScenario, frame, target,
                    bestByScenario);
            }
        }

        Console.WriteLine();
        Console.WriteLine("interp summary: out of range "
            + (outByFrame.TryGetValue("local", out int localOut) ? localOut : 0)
            + " of " + (totalByFrame.TryGetValue("local", out int localTotal) ? localTotal : 0)
            + " local cells, "
            + (outByFrame.TryGetValue(StudyShipFrameProductionLocal, out int worldOut) ? worldOut : 0)
            + " of " + (totalByFrame.TryGetValue(StudyShipFrameProductionLocal, out int worldTotal) ? worldTotal : 0)
            + " production-local cells (production singles count as out of range)");
        if (nonMonotoneSeries.Count == 0)
        {
            Console.WriteLine("interp monotonicity: no non-monotone series");
        }
        else
        {
            var familyKeys = new List<string>(nonMonotoneSeries.Keys);
            familyKeys.Sort(StringComparer.Ordinal);
            int droppedTotal = 0;
            int seriesTotal = 0;
            var sb = new StringBuilder();
            foreach (string key in familyKeys)
            {
                int series = nonMonotoneSeries[key];
                int dropped = nonMonotonePoints[key];
                droppedTotal += dropped;
                seriesTotal += series;
                sb.Append(' ').Append(key).Append(" (")
                    .Append(series.ToString(CultureInfo.InvariantCulture)).Append(" series, ")
                    .Append(dropped.ToString(CultureInfo.InvariantCulture)).Append(" points)");
            }

            Console.WriteLine("interp monotonicity: " + nonMonotoneSeries.Count
                + " non-monotone families ("
                + seriesTotal.ToString(CultureInfo.InvariantCulture) + " series), "
                + droppedTotal.ToString(CultureInfo.InvariantCulture)
                + " points dropped:" + sb);
        }

        Console.WriteLine();
        Console.WriteLine("headroom median over grid-limited discrete winners (ranked error / target'):");
        foreach (double target in new[] { 1d, 10d, 100d })
        {
            var line = new StringBuilder();
            foreach (string frame in new[] { StudyShipFrameLocal, StudyShipFrameProductionLocal })
            {
                string key = frame + "|" + target.ToString("R", CultureInfo.InvariantCulture);
                if (headroomByFrameTarget.TryGetValue(key, out List<double> values)
                    && values.Count > 0)
                {
                    values.Sort();
                    int n = values.Count;
                    double median = (n & 1) == 1
                        ? values[n / 2]
                        : 0.5d * (values[n / 2 - 1] + values[n / 2]);
                    line.Append(' ').Append(frame).Append('=')
                        .Append(median.ToString("E3", CultureInfo.InvariantCulture))
                        .Append(" (").Append(n.ToString(CultureInfo.InvariantCulture))
                        .Append(')');
                }
                else
                {
                    line.Append(' ').Append(frame).Append("=none");
                }
            }

            Console.WriteLine("  <= "
                + target.ToString("G", CultureInfo.InvariantCulture) + " m:" + line);
        }
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
        Dictionary<string, double> floors, Dictionary<string, double> formulations,
        string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario,frame,method,config,dp_max_m,dp_final_m,dv_max_mps,"
            + "da_m,de,denergy_ref,denergy_drift,raw_calls,calls,cost_rhs_eq,steps,ms,reps,"
            + "samples,mass_kg,ref_err_m,dp_ranked_m,at_floor,certified,formulation_m,finite,note");
        for (int i = 0; i < all.Count; i++)
        {
            StudyShipMatrixRow r = all[i];
            floors.TryGetValue(labels[i], out double floor);
            formulations.TryGetValue(labels[i], out double formulation);
            double ranked = StudyShipMatrixRanked(r, floor);
            int atFloor = double.IsFinite(floor) && double.IsFinite(r.Dp) && r.Dp < floor ? 1 : 0;
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
                .Append(StudyCsvNumber(r.DEnergyDrift)).Append(',')
                .Append(r.RawCalls.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Calls.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Cost.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Steps.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(StudyCsvNumber(r.Ms)).Append(',')
                .Append(r.Reps.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.SampleCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(StudyCsvNumber(r.Mass)).Append(',')
                .Append(StudyCsvNumber(double.IsFinite(floor) ? floor : double.NaN)).Append(',')
                .Append(StudyCsvNumber(ranked)).Append(',')
                .Append(atFloor.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(double.IsFinite(floor) ? "1" : "0").Append(',')
                .Append(StudyCsvNumber(double.IsNaN(formulation) ? double.NaN : formulation)).Append(',')
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
            // The independent world-frame dd golden disagrees with the local
            // DOP853 reference by ~8.5 m over 64 days (formulation sensitivity),
            // so a 1 m absolute claim is not supported. The 10 m target keeps the
            // scenario usable for integrator ranking without hiding that floor.
            c.AccuracyTarget = 10d;
            c.AlternativeTarget = 10d;
        }
        else if (c.Label.IndexOf("HEO", StringComparison.OrdinalIgnoreCase) >= 0
            || c.Label.IndexOf("hyperbolic", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            c.AccuracyTarget = 1d;
            c.AlternativeTarget = 1d;
        }
        else if (string.Equals(c.Label, "Earth LEO 400 km, 30 d", StringComparison.Ordinal))
        {
            // 1-day production chunks over 30 days: the local-frame carry-over
            // keeps the restart cost below the 1 m mission tolerance.
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

        // РРЅС„РѕСЂРјР°С†РёРѕРЅРЅР°СЏ С‡СѓРІСЃС‚РІРёС‚РµР»СЊРЅРѕСЃС‚СЊ Рє С„РѕСЂРјСѓР»РёСЂРѕРІРєРµ СЃРёР»С‹ РїСЂРё РґР°РЅРЅРѕР№
        // Р·Р°РїРµС‡С‘РЅРЅРѕР№ СЌС„РµРјРµСЂРёРґРµ, РЅРµ РјРµСЂР° РѕС€РёР±РєРё СЌС‚Р°Р»РѕРЅР° Рё РЅРµ РѕС€РёР±РєР° РёРЅС‚РµРіСЂР°С‚РѕСЂР°.
        // РќР° 1000 РІРёС‚РєР°С… 8.5 Рј (СЂРѕСЃС‚ РЅРµСЃС‚РµРїРµРЅРЅРѕР№, Р»РѕРєР°Р»СЊРЅС‹Рµ РїРѕРєР°Р·Р°С‚РµР»Рё 2.9-3.9);
        // РґРѕСЃС‚Р°С‚РѕС‡РЅРѕ СЃРёР» ~2e-13 Рј/СЃ^2 РґР»СЏ С‚Р°РєРѕРіРѕ СЃРґРІРёРіР° Р·Р° 64 СЃСѓС‚РѕРє.
        // Production Рё certified СЃРѕРіР»Р°СЃСѓСЋС‚СЃСЏ РЅР° 1.2 СЃРј, dd СЂРµС€Р°РµС‚ РћР”РЈ СЃ РґСЂСѓРіРёРј
        // РЅРµРїСЂСЏРјС‹Рј С‡Р»РµРЅРѕРј.
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

        bool hardEdgeBurn = StudyShipUnsplitPathNoClaim.Contains(c.Label);
        bool consistencyOnly = c.Label.IndexOf("Earth-Moon flyby", StringComparison.OrdinalIgnoreCase) >= 0;
        bool hyperbolic = c.Label.IndexOf("hyperbolic", StringComparison.OrdinalIgnoreCase) >= 0;
        bool certified = refs.LocalAsymptotic;

        StudyShipMatrixRow production = null;
        StudyShipMatrixRow productionUnsplit = null;
        StudyShipMatrixRow burnSplit = null;
        StudyShipMatrixRow localAdaptiveDop853 = null;
        StudyShipMatrixRow wh = null;
        StudyShipMatrixRow trialMass = null;
        foreach (StudyShipMatrixRow r in rows)
        {
            if (r.Method == "DOP853" && r.Frame == StudyShipFrameProductionLocal
                && r.Note == "production" && r.Finite)
            {
                productionUnsplit = r;
            }

            if (c.Thrust && r.Method == "DOP853" && r.Frame == StudyShipFrameProductionLocal
                && r.Note == "burn-split" && r.Finite)
            {
                burnSplit = r;
            }

            if (r.Method == "DOP853" && r.Frame == StudyShipFrameLocal && r.Steps < 0L
                && r.Finite && double.IsFinite(r.Dp)
                && (localAdaptiveDop853 == null || r.Dp < localAdaptiveDop853.Dp))
            {
                localAdaptiveDop853 = r;
            }

            if (r.Method == "WH-Y6" && r.Frame == StudyShipFrameLocal && r.Finite
                && double.IsFinite(r.Dp)
                && (wh == null || r.Dp < wh.Dp))
            {
                wh = r;
            }

            if (r.Method == "DOP853" && r.Frame == StudyShipFrameProductionLocal
                && r.Note == "trial-mass diagnostic")
            {
                trialMass = r;
            }
        }

        // The production path is the unsplit row. The burn-split row is a
        // diagnostic for prograde burns; only the hard-edge exception below
        // promotes it to the gated path.
        production = productionUnsplit;
        if (trialMass != null)
        {
            Console.WriteLine("INFO trial-mass diagnostic " + c.Label + ": "
                + trialMass.Config + " dp="
                + trialMass.Dp.ToString("E3", CultureInfo.InvariantCulture)
                + " m (not the production chunk; not gated)");
        }

        double refResolution = double.IsNaN(refs.LocalError) ? refs.LocalFinestDelta : refs.LocalError;
        // The independent dd endpoint floor can legitimately be larger than 10%
        // of the target. It is an uncertainty of the reference, not a reason to
        // hide it; require it to fit inside the target instead.
        double resolutionLimit = c.UseTaylorDdReference && refs.HasFormulation
            ? productionTarget
            : 0.1d * productionTarget;
        if (hardEdgeBurn)
        {
            // A hard on/off edge is C0, not C1. The unsplit production path is not
            // evidence; the edge-split path is the real path, and it can be gated
            // against the reference if that reference did reach an asymptotic pair.
            // The exception is explicit in StudyShipUnsplitPathNoClaim.
            if (refs.LocalAsymptotic)
            {
                Console.WriteLine("INFO hard-edge reference asymptotic: " + refs.LocalKind);
                Check(refResolution < resolutionLimit,
                    "matrix hard-edge reference resolution " + c.Label,
                    refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m < "
                    + resolutionLimit.ToString("G3", CultureInfo.InvariantCulture) + " m");
            }
            else
            {
                Console.WriteLine("INFO hard-edge burn reference (not certified): " + refs.LocalKind);
            }
        }
        else if (consistencyOnly && !(certified && refs.LocalResolved))
        {
            Console.WriteLine("no accuracy claim (uncertified reference): " + c.Label
                + " вЂ” " + refs.LocalKind);
        }
        else if (consistencyOnly)
        {
            // The seam-aware reference reached an asymptotic pair: the case can be
            // graded like any other, so fall through to the normal accuracy gate.
            Console.WriteLine("INFO Earth-Moon flyby reference certified by seam-aware breakpoints: "
                + refs.LocalKind);
            consistencyOnly = false;
            Check(certified || refs.LocalResolved,
                "matrix reference certification " + c.Label,
                refs.LocalKind);
            Check((certified || refs.LocalResolved) && refResolution < resolutionLimit,
                "matrix reference resolution " + c.Label,
                refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m < "
                + resolutionLimit.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }
        else
        {
            Check(certified || refs.LocalResolved,
                "matrix reference certification " + c.Label,
                refs.LocalKind);
            Check((certified || refs.LocalResolved) && refResolution < resolutionLimit,
                "matrix reference resolution " + c.Label,
                refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m < "
                + resolutionLimit.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

        if (consistencyOnly)
        {
            Check(!double.IsFinite(refs.ReferenceFloor),
                "matrix uncertified floor " + c.Label,
                double.IsFinite(refs.ReferenceFloor)
                    ? "uncertified case exposes a ranking floor "
                      + refs.ReferenceFloor.ToString("E3", CultureInfo.InvariantCulture) + " m"
                    : "no finite certified floor; accuracy/Pareto/scoreboard excluded");
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
            Console.WriteLine("no accuracy claim for the unsplit path: " + c.Label
                + " (explicit exception list); unsplit production dp="
                + (productionUnsplit == null ? "missing"
                    : productionUnsplit.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m")
                + ", burn-split dp="
                + (burnSplit == null ? "missing"
                    : burnSplit.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m")
                + "; split path gated below");
            Check(burnSplit != null && burnSplit.Finite
                    && burnSplit.Dp + refResolution <= productionTarget,
                "matrix hard-edge burn-split + reference " + c.Label,
                burnSplit == null ? "missing"
                    : burnSplit.Dp.ToString("E3", CultureInfo.InvariantCulture) + " + "
                      + refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
            // The split path is the gated path for a hard-edge burn; its velocity
            // and orbital elements must carry the same claims as the normal
            // production row, otherwise only its position was checked.
            Check(burnSplit != null && double.IsFinite(burnSplit.Dv) && burnSplit.Dv <= 1e-3d,
                "matrix hard-edge burn-split dv " + c.Label,
                burnSplit == null ? "missing"
                    : burnSplit.Dv.ToString("E3", CultureInfo.InvariantCulture) + " m/s <= 1E-03");
            Check(burnSplit != null && double.IsFinite(burnSplit.Da) && burnSplit.Da <= 0.1d,
                "matrix hard-edge burn-split da " + c.Label,
                burnSplit == null ? "missing"
                    : burnSplit.Da.ToString("E3", CultureInfo.InvariantCulture) + " m <= 0.1 m");
            Check(burnSplit != null && double.IsFinite(burnSplit.De) && burnSplit.De <= 1e-6d,
                "matrix hard-edge burn-split de " + c.Label,
                burnSplit == null ? "missing"
                    : burnSplit.De.ToString("E3", CultureInfo.InvariantCulture) + " <= 1E-06");
            Check(localAdaptiveDop853 != null
                    && localAdaptiveDop853.Dp <= alternativeTarget,
                "matrix hard-edge local adaptive DOP853 " + c.Label,
                localAdaptiveDop853 == null ? "missing"
                    : localAdaptiveDop853.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + alternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
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
            if (c.UseTaylorDdReference && refs.HasFormulation && double.IsFinite(refs.Formulation))
            {
                Check(production != null && production.Dp + refs.Formulation <= productionTarget,
                    "matrix production + independent floor " + c.Label,
                    production == null ? "missing"
                        : production.Dp.ToString("E3", CultureInfo.InvariantCulture) + " + "
                          + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                          + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
                Check(localAdaptiveDop853 != null
                        && localAdaptiveDop853.Dp + refs.Formulation <= alternativeTarget,
                    "matrix local adaptive + independent floor " + c.Label,
                    localAdaptiveDop853 == null ? "missing"
                        : localAdaptiveDop853.Dp.ToString("E3", CultureInfo.InvariantCulture) + " + "
                          + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                          + alternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
            }
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
                "matrix production final energy vs reference " + c.Label,
                production.DEnergy.ToString("E2", CultureInfo.InvariantCulture) + " < 1E-08");
        }

        // The production-local path and the local reference use the same force;
        // they differ in state carry (world round-trip vs local), chunking and
        // step control. Agreement is a frame/carry consistency check, not two
        // independent physical models.
        if (!c.Thrust && !consistencyOnly && production != null && localAdaptiveDop853 != null)
        {
            double separation = StudyShipRowSeparation(production, localAdaptiveDop853);
            Check(double.IsFinite(separation) && separation <= alternativeTarget,
                "matrix production vs local adaptive DOP853 " + c.Label,
                separation.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                + alternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

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

        // A row that lost samples cannot pass or win: every non-exceptional row
        // must carry exactly the reference checkpoint count, and its Note must say
        // what happened otherwise.
        int expectedSamples = refs.CheckpointTimes.Length;
        foreach (StudyShipMatrixRow r in rows)
        {
            bool expectedReject = hyperbolic && r.Method == "WH-Y6" && !r.Finite
                && (r.Note ?? "").IndexOf("unbound orbit", StringComparison.OrdinalIgnoreCase) >= 0;
            Check(expectedReject || r.SampleCount == expectedSamples,
                "matrix row sample count " + c.Label + " " + r.Method + "/" + r.Config,
                r.SampleCount.ToString(CultureInfo.InvariantCulture) + " of "
                + expectedSamples.ToString(CultureInfo.InvariantCulture)
                + (expectedReject ? " (expected WH reject)" : ""));
        }

        // A thrust row can only lose mass. This catches the previous
        // fixed-step splittings' sign error (they added the positive flow) even
        // when the trajectory is still finite; it also covers RK4/DOPRI5 and the
        // production path, not only the rows selected by the accuracy gates.
        if (c.Thrust)
        {
            double massLimit = c.Mass0 * (1d + 1e-12d);
            foreach (StudyShipMatrixRow r in rows)
            {
                Check(r.Finite && double.IsFinite(r.Mass) && r.Mass <= massLimit,
                    "matrix thrust mass " + c.Label + " " + r.Method + "/" + r.Config,
                    r.Finite
                        ? r.Mass.ToString("E6", CultureInfo.InvariantCulture) + " kg <= "
                          + c.Mass0.ToString("G6", CultureInfo.InvariantCulture) + " kg"
                        : "non-finite row");
            }
        }

        if (c.Thrust && !hardEdgeBurn)
        {
            Check(productionUnsplit != null && productionUnsplit.Finite
                    && productionUnsplit.Dp <= productionTarget,
                "matrix thrust " + c.WorldChunkSeconds.ToString("G", CultureInfo.InvariantCulture)
                + " s production chunk " + c.Label,
                productionUnsplit == null ? "missing production row"
                    : productionUnsplit.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
            if (burnSplit != null)
            {
                Console.WriteLine("INFO burn-split diagnostic " + c.Label + ": dp="
                    + burnSplit.Dp.ToString("E3", CultureInfo.InvariantCulture)
                    + " m (not the production path; not gated)");
            }
        }

        if (hyperbolic)
        {
            int whRows = 0;
            int whRejected = 0;
            foreach (StudyShipMatrixRow r in rows)
            {
                if (r.Method != "WH-Y6")
                {
                    continue;
                }

                whRows++;
                string note = r.Note ?? "";
                // The expected rejection is exactly InvalidOperationException
                // "WH: unbound orbit". Any other exception type or message is a
                // broken run, not an expected reject.
                bool noteIsUnbound = note.StartsWith("InvalidOperationException",
                        StringComparison.Ordinal)
                    && note.IndexOf("unbound orbit", StringComparison.OrdinalIgnoreCase) >= 0;
                bool ok = !r.Finite && noteIsUnbound;
                if (ok)
                {
                    whRejected++;
                }

                Check(ok,
                    "matrix WH unbound reject " + c.Label + " " + r.Config,
                    (r.Finite ? "finite" : "non-finite") + ", note="
                    + (note.Length > 0 ? note : "<empty>"));
            }

            Check(whRows > 0 && whRejected == whRows,
                "matrix WH unbound reject all rows " + c.Label,
                whRejected.ToString(CultureInfo.InvariantCulture) + " of "
                + whRows.ToString(CultureInfo.InvariantCulture) + " WH rows rejected as unbound");
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

    private static StudyShipComparisonRule[] StudyShipMatrixComparisonRules()
    {
        // Caps are at most 2x the measured deterministic call counts, not a
        // smoke threshold and not the old "~3x" claim (the real headroom was
        // 5-300x). A calls regression therefore fails; ms is handled separately
        // because it is noisy.
        return new[]
        {
            new StudyShipComparisonRule
            {
                Label = "Earth LEO 400 km circular", Target = 1d,
                CheapestCostCap = 1800L, WhToProductionRatio = 0.028d
            },
            new StudyShipComparisonRule
            {
                Label = "Earth LEO 400 km polar", Target = 1d,
                CheapestCostCap = 1800L, WhToProductionRatio = 0.028d
            },
            new StudyShipComparisonRule
            {
                // Measured 8,208 RHS-eq; cap 16,000 = 1.95x.
                Label = "Earth LEO 400 km sun-synchronous", Target = 1d,
                CheapestCostCap = 16000L, WhToProductionRatio = 0.25d
            },
            new StudyShipComparisonRule
            {
                // Measured 12,660 RHS-eq; cap 25,000 = 1.97x.
                Label = "Earth GEO 35786 km", Target = 2d,
                CheapestCostCap = 25000L, WhToProductionRatio = 0.14d
            },
            new StudyShipComparisonRule
            {
                // Measured 688 RHS-eq; cap 1,300 = 1.89x.
                Label = "Sun 1 AU circular", Target = 1d,
                CheapestCostCap = 1300L, WhToProductionRatio = 0.0006d
            },
            new StudyShipComparisonRule
            {
                // Measured 57,856 RHS-eq; cap 110,000 = 1.90x. Target is 10 m
                // because the independent dd golden's formulation floor is ~8.5 m.
                Label = "Earth LEO 400 km, 1000 orbits (~64 d)", Target = 10d,
                CheapestCostCap = 110000L, WhToProductionRatio = 0.028d
            },
            new StudyShipComparisonRule
            {
                // Measured 27,392 RHS-eq; cap 54,000 = 1.97x.
                Label = "Earth LEO 400 km, 30 d", Target = 1d,
                CheapestCostCap = 54000L, WhToProductionRatio = 0.035d
            }
        };
    }

    private static bool StudyShipMatrixMsComparable(
        StudyShipMatrixRow a, StudyShipMatrixRow b)
    {
        return a != null && b != null && a.Reps == b.Reps
            && double.IsFinite(a.Ms) && double.IsFinite(b.Ms)
            && a.Ms >= 0d && b.Ms >= 0d;
    }

    private static void StudyShipMatrixMsGate(
        string name, StudyShipMatrixRow a, StudyShipMatrixRow b, double factor, string what)
    {
        if (!StudyShipMatrixMsComparable(a, b))
        {
            Console.WriteLine("WARN " + name + ": ms not gated (different measurement modes: reps "
                + (a == null ? "missing" : a.Reps.ToString(CultureInfo.InvariantCulture))
                + " vs " + (b == null ? "missing" : b.Reps.ToString(CultureInfo.InvariantCulture))
                + "); " + what);
            return;
        }

        Check(a.Ms <= factor * b.Ms, name,
            a.Ms.ToString("F1", CultureInfo.InvariantCulture) + " ms <= "
            + (factor == 1d ? "" : factor.ToString("G2", CultureInfo.InvariantCulture) + " x ")
            + b.Ms.ToString("F1", CultureInfo.InvariantCulture) + " ms (same reps="
            + a.Reps.ToString(CultureInfo.InvariantCulture) + "); " + what);
    }

    private static void StudyShipMatrixComparisons(
        List<StudyShipMatrixRow> all, List<string> labels, Dictionary<string, double> floors)
    {
        // The suite used to only check "no regression". These statements are the
        // actual study claim: which family is cheapest at a useful accuracy, and
        // by how much it beats the production path. Calls are deterministic and
        // gated tightly; ms is gated only as a same-process ratio with the same
        // repetition count, otherwise it is a warning.
        Console.WriteLine();
        Console.WriteLine("=== integrator comparison claims (failing gates) ===");
        var rules = StudyShipMatrixComparisonRules();

        foreach (StudyShipComparisonRule rule in rules)
        {
            floors.TryGetValue(rule.Label, out double floor);
            if (!double.IsFinite(floor))
            {
                // An uncertified reference cannot support an accuracy comparison:
                // Dp is then a difference from an invalid answer, not an error.
                Console.WriteLine("comparison " + rule.Label
                    + ": skipped, the reference is not certified");
                continue;
            }

            double target = Math.Max(rule.Target, floor);
            if (floor > rule.Target)
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
                if (r.Method == "DOP853" && r.Frame == StudyShipFrameProductionLocal
                    && r.Note == "production" && r.Finite)
                {
                    production = r;
                }

                double ranked = StudyShipMatrixRanked(r, floor);
                if (r.Frame != StudyShipFrameLocal
                    || !StudyShipMatrixRankable(r, StudyShipFrameLocal) || !r.Finite
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

                // Only min-of-3 rows are comparable with each other. A single-rep
                // row is still in the table, but its ms is not used as the measured
                // wall-clock winner.
                if (r.Reps >= 3
                    && (cheapestMs == null || r.Ms < cheapestMs.Ms
                        || (r.Ms == cheapestMs.Ms
                            && ranked < StudyShipMatrixRanked(cheapestMs, floor))))
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
                // does not hold in the resource the user actually spends. The ms
                // comparison is only valid when both rows used the same repetition
                // mode; otherwise it is printed as a warning, not gated.
                StudyShipMatrixMsGate(
                    "comparison cheapest cost vs measured ms " + rule.Label,
                    cheapest, cheapestMs, 3d,
                    "cost winner " + cheapest.Method + "/" + cheapest.Config + " vs ms winner "
                    + (cheapestMs == null ? "none" : cheapestMs.Method + "/" + cheapestMs.Config));
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
                    // resource win. ms is only compared in the same repetition
                    // mode; otherwise it is a warning, not a gate.
                    StudyShipMatrixMsGate(
                        "comparison WH vs production ms " + rule.Label,
                        cheapestWh, production, 2d,
                        "WH " + cheapestWh.Method + "/" + cheapestWh.Config);
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

    private static void StudyShipMatrixClaimCoverage(List<StudyShipMatrixCase> cases)
    {
        var covered = new HashSet<string>(StudyShipMatrixBaselines.Keys, StringComparer.Ordinal);
        foreach (StudyShipComparisonRule rule in StudyShipMatrixComparisonRules())
        {
            covered.Add(rule.Label);
        }

        foreach (StudyShipMatrixCase c in cases)
        {
            bool hasClaim = covered.Contains(c.Label);
            string reason;
            bool hasNoClaim = StudyShipNoClaimReasons.TryGetValue(c.Label, out reason);
            Check(hasClaim || hasNoClaim,
                "matrix claim coverage " + c.Label,
                hasClaim ? "baseline/comparison entry present"
                    : hasNoClaim ? "no claim: " + reason
                    : "missing baseline, comparison and no-claim entry");
        }

        foreach (var kv in StudyShipNoClaimReasons)
        {
            Console.WriteLine("no claim: " + kv.Key + " вЂ” " + kv.Value);
        }
    }

    private static void StudyShipMatrixRegression(
        List<StudyShipMatrixRow> all, List<string> labels,
        Dictionary<string, double> formulations, Dictionary<string, double> floors)
    {
        Console.WriteLine();
        Console.WriteLine("=== regression report (3x rebased baseline on the max-over-trajectory metric; regression lock, not an accuracy claim) ===");
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
                if (r.Method == "DOP853" && r.Frame == StudyShipFrameProductionLocal
                    && r.Note == "production" && r.Finite)
                {
                    production = r;
                }

                if (r.Frame == StudyShipFrameLocal && StudyShipMatrixRankable(r, StudyShipFrameLocal)
                    && r.Finite && double.IsFinite(r.Dp))
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

    private static List<StudyShipMatrixCase> StudyShipMatrixBuildCases(
        StarSystem gravity, StarSystem j2OnlySystem, StarSystem dragOnlySystem)
    {
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
            // the suite (the local force is much cheaper than the production-local path).
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
            // The Moon is the dominant body inside its SOI, so the flyby is
            // integrated and graded in the Moon-centered local frame. This is
            // exactly the frame the production step uses after FindDominantBody
            // switches to the Moon; the old Earth-centered version was badly
            // conditioned and its fixed-step reference never converged.
            var flyby = new StudyShipMatrixCase
            {
                Label = "Earth-Moon flyby inside lunar SOI (Moon-centered)",
                Sys = gravity,
                Central = 2,
                P0 = new Vector3d(0d, 2e7d, 0d),
                V0 = new Vector3d(0d, 0d, 900d),
                Mass0 = 1000d,
                Seconds = 2d * day,
                AllowTaylor = true,
                Tau = 20000d,
                StepScale = 0.25d,
                ScaleLength = 2e7d,
                CertifiedReferenceStepSeconds = 40000d,
                // The baked ephemeris has slope changes at segment seams. A fixed
                // step straddling one breaks the Richardson pair; splitting exactly
                // on the seams makes the reference honest instead of grading against
                // a secret kink. The Moon-centered window has 0 seams, so this is
                // harmless here and protects the case if the bake window changes.
                SeamAwareReference = true
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
            var chunk = StudyShipMatrixCaseOrbit("Earth LEO 400 km, 30 d",
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
                ThrustRampSeconds = 1d,
                AllowTaylor = false,
                WorldChunkSeconds = 60d,
                Tau = StudyShipMatrixTau(6.371e6d + 4e5d,
                    gravity.AllBodies[1].ResolveStandardGravitationalParameter()),
                StepScale = 0.02d,
                ScaleLength = 6.371e6d + 4e5d
            });
        }
        StudyShipMatrixApplyTags(cases);
        return cases;
    }

    /// <summary>
    /// Fill the mode metadata used by the mode-map report. The scenario builder is
    /// the single place that knows the physical regime, so tags live here rather
    /// than in a separate hand-written table.
    /// </summary>
    private static void StudyShipMatrixApplyTags(List<StudyShipMatrixCase> cases)
    {
        foreach (StudyShipMatrixCase c in cases)
        {
            OrbitingBody central = c.Sys.AllBodies[c.Central];
            c.AltitudeKm = (c.P0.Magnitude - central.Radius) / 1000d;
            c.SpeedMps = c.V0.Magnitude;
            c.Eccentricity = OrbitalElements.FromState(
                c.P0, c.V0, central.ResolveStandardGravitationalParameter()).Eccentricity;
            c.HasBreakpoints = c.Thrust;
            c.ModeTags = new string[0];

            switch (c.Label)
            {
                case "Earth LEO 400 km circular":
                    c.Mode = "pure-circular";
                    c.ModeTags = new[] { "earth", "leo", "400km", "circular", "pure-gravity", "central" };
                    break;
                case "Earth LEO 400 km polar":
                    c.Mode = "pure-circular";
                    c.ModeTags = new[] { "earth", "leo", "400km", "circular", "polar", "pure-gravity", "central" };
                    break;
                case "Earth LEO 400 km sun-synchronous":
                    c.Mode = "j2";
                    c.ModeTags = new[] { "earth", "leo", "400km", "circular", "j2", "sso", "central" };
                    break;
                case "Earth GTO 400 x 40000 km":
                    c.Mode = "eccentric";
                    c.ModeTags = new[] { "earth", "gto", "elliptic", "eccentric", "pure-gravity", "central" };
                    break;
                case "Earth Molniya 500 x 40000 km":
                    c.Mode = "eccentric";
                    c.ModeTags = new[] { "earth", "molniya", "elliptic", "eccentric", "high-eccentricity", "pure-gravity", "central" };
                    break;
                case "Earth GEO 35786 km":
                    c.Mode = "pure-circular";
                    c.ModeTags = new[] { "earth", "geo", "circular", "long", "pure-gravity", "central" };
                    break;
                case "Sun 1 AU circular":
                    c.Mode = "heliocentric";
                    c.ModeTags = new[] { "sun", "1au", "circular", "long", "pure-gravity", "central" };
                    break;
                case "Sun 0.5 x 2 AU eccentric":
                    c.Mode = "heliocentric-eccentric";
                    c.ModeTags = new[] { "sun", "elliptic", "eccentric", "long", "pure-gravity", "central" };
                    break;
                case "Earth 400 km + J2":
                    c.Mode = "j2";
                    c.ModeTags = new[] { "earth", "leo", "400km", "circular", "j2", "central" };
                    break;
                case "Earth 250 km + atmosphere drag":
                    c.Mode = "drag";
                    c.ModeTags = new[] { "earth", "leo", "250km", "circular", "drag", "atmosphere", "non-conservative" };
                    break;
                case "Earth LEO 400 km + 600 s prograde burn":
                    c.Mode = "thrust";
                    c.ModeTags = new[] { "earth", "leo", "400km", "burn", "thrust", "variable-mass", "has-breakpoints", "non-conservative" };
                    break;
                case "Earth-Moon flyby inside lunar SOI (Moon-centered)":
                    c.Mode = "flyby";
                    c.ModeTags = new[] { "moon", "soi", "flyby", "pure-gravity", "central", "close-approach" };
                    break;
                case "Earth LEO 400 km, 1000 orbits (~64 d)":
                    c.Mode = "pure-circular-long";
                    c.ModeTags = new[] { "earth", "leo", "400km", "circular", "long", "1000-orbits", "pure-gravity", "central" };
                    break;
                case "Earth HEO 300 x 120000 km":
                    c.Mode = "eccentric";
                    c.ModeTags = new[] { "earth", "heo", "elliptic", "eccentric", "high-eccentricity", "pure-gravity", "central" };
                    break;
                case "Earth hyperbolic flyby e=1.5":
                    c.Mode = "flyby";
                    c.ModeTags = new[] { "earth", "hyperbolic", "flyby", "pure-gravity", "central", "tight" };
                    break;
                case "Earth LEO 400 km, 30 d":
                    c.Mode = "pure-circular-long";
                    c.ModeTags = new[] { "earth", "leo", "400km", "circular", "long", "30d", "pure-gravity", "central" };
                    break;
                case "Earth LEO 400 km + unaligned 600 s burn":
                    c.Mode = "thrust";
                    c.ModeTags = new[] { "earth", "leo", "400km", "burn", "thrust", "variable-mass", "has-breakpoints", "non-conservative", "unaligned" };
                    break;
                default:
                    c.Mode = "other";
                    c.ModeTags = new[] { "other" };
                    break;
            }
        }
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
        List<StudyShipMatrixCase> cases = StudyShipMatrixBuildCases(gravity, j2OnlySystem, dragOnlySystem);

        StudyShipMatrixClaimCoverage(cases);

        string resultsDir = StudyShipMatrixResultsDir();
        string csvPath = Path.Combine(resultsDir, "Test549_SpacecraftOrbitMatrix.csv");
        var allRows = new List<StudyShipMatrixRow>();
        var allLabels = new List<string>();
        var formulations = new Dictionary<string, double>();
        var floors = new Dictionary<string, double>();
        var certifiedScenarios = new HashSet<string>(StringComparer.Ordinal);
        int referenceFailures = 0;

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
                // Never turn a failed case into a pass: record the exception type
                // and message, fail an explicit gate and count the skipped case.
                referenceFailures++;
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
            if (refs.LocalAsymptotic)
            {
                certifiedScenarios.Add(c.Label);
            }
            caseWatch.Stop();
            Console.WriteLine("case time: " + caseWatch.ElapsedMilliseconds + " ms");
        }

        Check(referenceFailures == 0 && cases.Count > 0,
            "matrix all cases executed",
            referenceFailures.ToString(CultureInfo.InvariantCulture) + " reference failure(s) of "
            + cases.Count.ToString(CultureInfo.InvariantCulture) + " case(s)");

        StudyShipMatrixOrderAndTolerance();
        StudyShipMatrixCsv(allRows, allLabels, floors, formulations, csvPath);
        StudyShipMatrixScoreboard(allRows, allLabels, floors, certifiedScenarios);
        if (StudyShipMatrixInterpScore)
        {
            StudyShipMatrixInterpScoreboard(allRows, allLabels, floors, certifiedScenarios);
        }

        StudyShipMatrixComparisons(allRows, allLabels, floors);
        StudyShipMatrixRegression(allRows, allLabels, formulations, floors);
        Console.WriteLine("failed checks: " + (failures - failuresBefore));
        Console.WriteLine("--- Spacecraft orbit integrator matrix complete ---");
        return 0;
    }

}
